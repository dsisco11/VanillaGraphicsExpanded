using VanillaGraphicsExpanded.Rendering.RHI;
using System;
using System.Runtime.InteropServices;
using VanillaGraphicsExpanded.Rendering.Contracts;
using VanillaGraphicsExpanded.Rendering.Pipeline.Passes;
using VanillaGraphicsExpanded.Rendering.Spirv;
using Legacy = OpenTK.Graphics.OpenGL;

namespace VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;

/// <summary>Validates complete captured resources against fixed executable facts before any native slot publication.</summary>
internal sealed partial class OpenGLBackend
{
    #region Private
    #region Complete set
    /// <summary>Rechecks resource usability on every execution while retaining successful compatibility history.</summary>
    private void ValidateResourceBindings(RHIShader shader, RHIResourceBindings inputs)
    {
        var prepared = shader.Interface.PreparedBindings;
        if (!inputs.SchemaValidated)
        {
            foreach (var input in inputs.Samplers)
            {
                var entry = prepared.Resolve(input.Identity);
                if (entry.Contract.Name is null) continue; // A property excluded by this immutable permutation has no slot effect.
                if (entry.Contract.Kind != ShaderBindingKind.Sampler || input.Element >= entry.ActiveElements.Count)
                    throw new ArgumentException("Sampler input does not match its prepared contract.");
            }
            foreach (var input in inputs.Images) RequireInputKind(prepared, input.Identity, ShaderBindingKind.Image);
            foreach (var input in inputs.Uniforms) RequireInputKind(prepared, input.Identity, ShaderBindingKind.UniformBlock);
            foreach (var input in inputs.Buffers)
                RequireInputKind(prepared, input.Identity, BufferKind(input.Role));
            inputs.SchemaValidated = true;
        }
        foreach (var active in prepared.ActiveEntries)
        {
            var binding = active.Binding;
            ref var history = ref CollectionsMarshal.GetValueRefOrAddDefault(inputs.ValidationHistory,
                (active.Identity, active.Element), out _);
            switch (binding.Contract.Kind)
            {
                case ShaderBindingKind.Sampler:
                    inputs.TrySampler(active.Identity, active.Element, out var sampler);
                    if (!sampler.Texture.IsEmpty && !GpuResourceManager.ValidateTextureReferenceInput(sampler.Texture.Resource))
                        throw new InvalidOperationException("Captured sampler storage was retired or became unavailable.");
                    ShaderPreparedSubmission.ValidateSampler(binding, sampler.Texture, ref history);
                    if (!sampler.Sampler.IsEmpty)
                        _ = GpuResourceManager.RequireSamplerReferenceMetadata(sampler.Sampler.RequireResource());
                    break;
                case ShaderBindingKind.Image:
                    inputs.TryImage(active.Identity, out var image);
                    var view = ResolveImageView(binding, image);
                    if (!view.Texture.IsEmpty && !GpuResourceManager.ValidateTextureReferenceInput(view.Texture.Resource))
                        throw new InvalidOperationException("Captured image storage was retired or became unavailable.");
                    ShaderPreparedSubmission.ValidateImage(binding, view, ref history);
                    if (!view.Texture.IsEmpty) RenderPass.ValidateImageTexture(view);
                    break;
                case ShaderBindingKind.UniformBlock:
                    if (inputs.TryUniform(active.Identity, out var uniform)) ValidateUniformSnapshot(binding, uniform.Snapshot);
                    else { inputs.TryBuffer(active.Identity, out var buffer); ValidateBufferInput(binding, buffer); }
                    break;
                case ShaderBindingKind.StorageBlock:
                case ShaderBindingKind.AtomicCounter:
                    inputs.TryBuffer(active.Identity, out var storage);
                    ValidateBufferInput(binding, storage);
                    break;
            }
        }
    }
    /// <summary>Checks represented entry kinds while preserving intentionally excluded permutation inputs.</summary>
    private static void RequireInputKind(GpuPreparedBindings prepared, ulong identity, ShaderBindingKind kind)
    {
        var entry = prepared.Resolve(identity);
        if (entry.Contract.Name is not null && entry.Contract.Kind != kind)
            throw new ArgumentException("Resource input kind does not match its prepared contract.");
    }
    #endregion
    #region Uniforms and buffers
    /// <summary>Checks captured lifetime and minimum extent without consuming dirty work or reserving storage.</summary>
    private static void ValidateUniformSnapshot(GpuPreparedBindings.Entry binding, RHIUniformSnapshot? snapshot)
    {
        if (snapshot is null) { RequireInput(binding, false); return; }
        snapshot.RequireUsable();
        if (snapshot.Bytes.Length < binding.MinimumSize)
            throw new ArgumentException("Captured uniform bytes are smaller than the prepared block.");
        if (!GpuUniformRingSystem.TryGetCurrent(out var ring))
            throw new InvalidOperationException("Captured uniform publication requires an open allocation epoch.");
        ring.RequireOpenPublication();
    }
    /// <summary>Validates known capacity and declared alignment without reading buffer contents.</summary>
    private void ValidateBufferInput(GpuPreparedBindings.Entry binding, RHIBufferBinding input)
    {
        if (input.Buffer.IsEmpty) { RequireInput(binding, false); return; }
        var resource = input.Buffer.RequireResource();
        var data = GpuResourceManager.RequireBufferReferenceMetadata(resource);
        nint length = input.Whole ? data.Capacity : input.Length;
        _ = RequireRange(input.Buffer, input.Offset, length);
        int alignment = binding.Contract.Kind switch
        {
            ShaderBindingKind.UniformBlock => Math.Max(1, Capabilities.UniformBufferOffsetAlignment),
            ShaderBindingKind.StorageBlock => Math.Max(1, Capabilities.ShaderStorageBufferOffsetAlignment),
            _ => 4
        };
        int minimum = binding.Contract.Kind == ShaderBindingKind.AtomicCounter ? Math.Max(4, binding.MinimumSize) : binding.MinimumSize;
        if (length < minimum || input.Offset % alignment != 0
            || binding.Contract.Kind == ShaderBindingKind.UniformBlock && length > Capabilities.MaxUniformBlockSize)
            throw new ArgumentException($"Captured {binding.Contract.Name} buffer extent {length} (minimum {minimum}, offset {input.Offset}, alignment {alignment}) is incompatible with its prepared block.");
    }
    /// <summary>Maps the three semantic buffer roles accepted by immutable resource tables.</summary>
    private static ShaderBindingKind BufferKind(BufferRole role) => role switch
    {
        BufferRole.Uniform => ShaderBindingKind.UniformBlock,
        BufferRole.ShaderStorage => ShaderBindingKind.StorageBlock,
        BufferRole.AtomicCounter => ShaderBindingKind.AtomicCounter,
        _ => throw new ArgumentOutOfRangeException(nameof(role))
    };
    /// <summary>Rejects absent required resources; optional absent inputs are explicitly cleared during execution.</summary>
    private static void RequireInput(GpuPreparedBindings.Entry binding, bool present)
    {
        if (!present && binding.Contract.Binding.Required)
            throw new InvalidOperationException($"Shader requires a captured {binding.Contract.Name} resource.");
    }
    #endregion
    #region Image projection
    /// <summary>Projects the semantic selection for the retained metadata validator, never choosing a native execution variant.</summary>
    private static RHIImageBinding ResolveImageView(GpuPreparedBindings.Entry binding, RHIImageInput input)
    {
        var view = input.View;
        bool layered = view.Layered;
        if (input.DefaultView)
        {
            var target = (TextureDimension)GpuPreparedBindings.CompatibleTextureTarget(binding.Type);
            layered = target is TextureDimension.Three or TextureDimension.TwoArray
                or TextureDimension.Cube or TextureDimension.OneArray
                or TextureDimension.CubeArray or TextureDimension.TwoMultisampleArray;
        }
        return view with { Layered = layered };
    }
    #endregion
    #endregion
}
