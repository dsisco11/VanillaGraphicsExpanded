using VanillaGraphicsExpanded.Rendering.RHI;
using System;
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
    /// <summary>Checks current availability and acquires guards in one walk of the retained active inputs.</summary>
    private void ValidateResourceBindings(OpenGLBindingPlan plan)
    {
        foreach (var input in plan.Inputs)
        {
            var binding = input.Binding;
            var previous = input.History;
            switch (binding.Contract.Kind)
            {
                case ShaderBindingKind.Sampler:
                    var sampler = input.Sampler;
                    if (!sampler.Texture.IsEmpty && !RHIResourceManager.ValidateTextureReferenceInput(sampler.Texture.Resource))
                        throw new InvalidOperationException("Captured sampler storage was retired or became unavailable.");
                    ShaderPreparedSubmission.ValidateSampler(binding, sampler.Texture, ref input.History);
                    if (!sampler.Texture.IsEmpty && !previous.HasSameResult(input.History)) plan.InputsChanged = true;
                    RetainExecutionResource(sampler.Texture.Resource);
                    int samplerName;
                    if (!sampler.Sampler.IsEmpty)
                    {
                        var owner = sampler.Sampler.RequireResource();
                        _ = RHIResourceManager.RequireSamplerReferenceMetadata(owner);
                        RetainExecutionResource(owner);
                        samplerName = checked((int)owner.ResourceId);
                    }
                    else samplerName = ResolvePolicySampler((ShaderSamplerPolicy)binding.Contract.Binding.Sampler);
                    if (input.NativeSampler != samplerName) plan.InputsChanged = true;
                    input.NativeSampler = samplerName;
                    break;
                case ShaderBindingKind.Image:
                    var view = input.Image;
                    if (!view.Texture.IsEmpty && !RHIResourceManager.ValidateTextureReferenceInput(view.Texture.Resource))
                        throw new InvalidOperationException("Captured image storage was retired or became unavailable.");
                    ShaderPreparedSubmission.ValidateImage(binding, view, ref input.History);
                    if (!view.Texture.IsEmpty && !previous.HasSameResult(input.History)) plan.InputsChanged = true;
                    if (!view.Texture.IsEmpty) RenderPass.ValidateImageTexture(view);
                    RetainExecutionResource(view.Texture.Resource);
                    break;
                case ShaderBindingKind.UniformBlock:
                    if (input.HasUniform) ValidateUniformSnapshot(binding, input.Uniform);
                    else ValidateAndRetainBuffer(plan, input);
                    break;
                case ShaderBindingKind.StorageBlock:
                case ShaderBindingKind.AtomicCounter:
                    ValidateAndRetainBuffer(plan, input);
                    break;
            }
        }
    }
    /// <summary>Protects a validated original buffer through publication and dispatch.</summary>
    private void ValidateAndRetainBuffer(OpenGLBindingPlan plan, OpenGLBindingPlan.Input input)
    {
        ValidateBufferInput(input.Binding, input.Buffer);
        if (input.Buffer.Buffer.IsEmpty) return;
        var resource = input.Buffer.Buffer.RequireResource();
        var metadata = RHIResourceManager.RequireBufferReferenceMetadata(resource);
        if (input.BufferIdentity != resource.ResourceId || input.BufferMetadata != metadata) plan.InputsChanged = true;
        input.BufferIdentity = resource.ResourceId;
        input.BufferMetadata = metadata;
        RetainExecutionResource(resource);
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
        if (!RHIUniformRingSystem.TryGetCurrent(out var ring))
            throw new InvalidOperationException("Captured uniform publication requires an open allocation epoch.");
        ring.RequireOpenPublication();
    }
    /// <summary>Validates known capacity and declared alignment without reading buffer contents.</summary>
    private void ValidateBufferInput(GpuPreparedBindings.Entry binding, RHIBufferBinding input)
    {
        if (input.Buffer.IsEmpty) { RequireInput(binding, false); return; }
        var resource = input.Buffer.RequireResource();
        var data = RHIResourceManager.RequireBufferReferenceMetadata(resource);
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
    /// <summary>Rejects absent required resources; optional absent inputs are explicitly cleared during execution.</summary>
    private static void RequireInput(GpuPreparedBindings.Entry binding, bool present)
    {
        if (!present && binding.Contract.Binding.Required)
            throw new InvalidOperationException($"Shader requires a captured {binding.Contract.Name} resource.");
    }
    #endregion

    #endregion
}
