using System;
using System.Collections.Generic;
using VanillaGraphicsExpanded.Rendering.Contracts;
using VanillaGraphicsExpanded.Rendering.Spirv;

namespace VanillaGraphicsExpanded.Rendering.RHI;

/// <summary>Owns shader-specific structural preparation independently of captured uniform versions.</summary>
internal sealed class RHIResourceBindingStructure
{
    /// <summary>Retains one active semantic selection; native realization belongs to the backend.</summary>
    internal sealed class Input
    {
        internal readonly GpuPreparedBindings.Entry Binding;
        internal readonly ulong Identity;
        internal readonly RHISamplerBinding Sampler;
        internal RHIImageBinding Image;
        internal readonly RHIBufferBinding Buffer;
        internal readonly bool HasUniform;

        /// <summary>Projects a fixed active array element and shader-defined default image layering.</summary>
        internal Input(GpuPreparedBindings.ActiveEntry active, RHIResourceBindings inputs)
        {
            Binding = active.Binding;
            Identity = active.Identity;
            inputs.TrySampler(active.Identity, active.Element, out Sampler);
            inputs.TryImage(active.Identity, out var image);
            var target = image.DefaultView ? (TextureDimension)GpuPreparedBindings.CompatibleTextureTarget(Binding.Type) : default;
            Image = image.View with { Layered = image.DefaultView ? target is TextureDimension.Three or TextureDimension.TwoArray
                or TextureDimension.Cube or TextureDimension.OneArray or TextureDimension.CubeArray or TextureDimension.TwoMultisampleArray : image.View.Layered };
            inputs.TryBuffer(active.Identity, out Buffer);
            HasUniform = inputs.TryUniform(active.Identity, out _);
        }
    }

    private readonly RHIShader shader;
    private bool dirty = true;
    private Exception? failure;
    private IRHIContextLifetime? origin;
    internal readonly Input[] Inputs;
    internal readonly GpuPreparedBindings Interface;
    internal ulong Revision { get; private set; }
#if DEBUG
    /// <summary>Counts initial and relevant-change preparation attempts, including withdrawn failures.</summary>
    internal long Preparations { get; private set; }
#endif

    #region Internal API
    /// <summary>Freezes active schema without native calls, allocator work or resource metadata acquisition.</summary>
    internal RHIResourceBindingStructure(RHIResourceBindings inputs)
    {
        shader = inputs.Shader.SelectedShader;
        Interface = shader.Interface.PreparedBindings;
        foreach (var value in inputs.Samplers)
        {
            var entry = Interface.Resolve(value.Identity);
            if (entry.Contract.Name is not null && (entry.Contract.Kind != ShaderBindingKind.Sampler || value.Element >= entry.ActiveElements.Count))
                throw new ArgumentException("Sampler input does not match its prepared contract.");
        }
        foreach (var value in inputs.Images) RequireKind(value.Identity, ShaderBindingKind.Image);
        foreach (var value in inputs.Uniforms) RequireKind(value.Identity, ShaderBindingKind.UniformBlock);
        foreach (var value in inputs.Buffers) RequireKind(value.Identity, value.Role switch
        {
            BufferRole.Uniform => ShaderBindingKind.UniformBlock,
            BufferRole.ShaderStorage => ShaderBindingKind.StorageBlock,
            _ => ShaderBindingKind.AtomicCounter
        });
        var all = new List<Input>();
        foreach (var active in Interface.ActiveEntries)
        {
            var input = new Input(active, inputs);
            all.Add(input);
            Observe(input.Sampler.Texture.Resource);
            if (!input.Sampler.Sampler.IsEmpty) Observe(input.Sampler.Sampler.RequireResource());
            Observe(input.Image.Texture.Resource);
            if (!input.Buffer.Buffer.IsEmpty) Observe(input.Buffer.Buffer.RequireResource());
        }
        Inputs = all.ToArray();
        Observe(shader.Program);
    }

    /// <summary>Establishes compatibility initially or after a relevant committed change, retaining failure until another change.</summary>
    internal void Prepare(RHICapabilities capabilities, IRHIContextLifetime context)
    {
        origin ??= context;
        origin.RequireContext();
        if (dirty)
        {
            dirty = false;
            failure = null;
#if DEBUG
            Preparations++;
#endif
            try
            {
                shader.RequireUsable();
                foreach (var input in Inputs) PrepareInput(input, capabilities);
            }
            catch (Exception error) { failure = error; }
        }
        if (failure is not null) throw new InvalidOperationException("Resource binding preparation is unavailable.", failure);
    }
    /// <summary>Connects an actual active dependency without owning it or retaining abandoned consumers.</summary>
    internal void Observe(GpuResource? resource) => resource?.ObservePreparation(this, Invalidate);

    #endregion

    #region Private
    /// <summary>Withdraws derived facts only; ordinary content writes emit no structural change.</summary>
    private void Invalidate() { dirty = true; Revision++; }

    /// <summary>Rejects represented kind mismatches while allowing excluded permutation properties.</summary>
    private void RequireKind(ulong identity, ShaderBindingKind kind)
    {
        var entry = Interface.Resolve(identity);
        if (entry.Contract.Name is not null && entry.Contract.Kind != kind)
            throw new ArgumentException("Resource input kind does not match its prepared contract.");
    }

    /// <summary>Establishes resource relationships from canonical facts before backend realization or publication.</summary>
    private static void PrepareInput(Input input, RHICapabilities capabilities)
    {
        switch (input.Binding.Contract.Kind)
        {
            case ShaderBindingKind.Sampler:
                ShaderResourcePreparation.ValidateSampler(input.Binding, input.Sampler.Texture);
                if (!input.Sampler.Sampler.IsEmpty) _ = RHIResourceManager.RequireSamplerReferenceMetadata(input.Sampler.Sampler.RequireResource());
                break;
            case ShaderBindingKind.Image:
                ShaderResourcePreparation.ValidateImage(input.Binding, input.Image);
                break;
            case ShaderBindingKind.UniformBlock when input.HasUniform:
                break;
            default:
                var selection = input.Buffer;
                if (selection.Buffer.IsEmpty)
                {
                    if (input.Binding.Contract.Binding.Required) throw new InvalidOperationException("A required buffer is absent.");
                    break;
                }
                var metadata = RHIResourceManager.RequireBufferReferenceMetadata(selection.Buffer.RequireResource());
                nint length = selection.Whole ? metadata.Capacity : selection.Length;
                int alignment = input.Binding.Contract.Kind switch
                {
                    ShaderBindingKind.UniformBlock => Math.Max(1, capabilities.UniformBufferOffsetAlignment),
                    ShaderBindingKind.StorageBlock => Math.Max(1, capabilities.ShaderStorageBufferOffsetAlignment),
                    _ => 4
                };
                int minimum = input.Binding.Contract.Kind == ShaderBindingKind.AtomicCounter ? Math.Max(4, input.Binding.MinimumSize) : input.Binding.MinimumSize;
                if (selection.Offset < 0 || length < 0 || selection.Offset > metadata.Capacity - length || length < minimum
                    || selection.Offset % alignment != 0 || input.Binding.Contract.Kind == ShaderBindingKind.UniformBlock && length > capabilities.MaxUniformBlockSize)
                    throw new ArgumentException("Buffer selection is incompatible with its shader block.");
                break;
        }
    }
    #endregion
}
