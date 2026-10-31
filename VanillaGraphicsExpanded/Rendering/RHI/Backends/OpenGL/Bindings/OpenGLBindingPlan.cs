using System;
using System.Collections.Generic;
using VanillaGraphicsExpanded.Rendering.Contracts;
using VanillaGraphicsExpanded.Rendering.Spirv;
using VanillaGraphicsExpanded.Rendering.Uniforms;

namespace VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;

/// <summary>Compiles immutable authored inputs into active execution entries without retaining native binding knowledge.</summary>
internal sealed class OpenGLBindingPlan
{
    /// <summary>Retains one active input and its successful metadata compatibility history.</summary>
    internal sealed class Input
    {
        internal readonly GpuPreparedBindings.Entry Binding;
        internal readonly RHISamplerBinding Sampler;
        internal readonly RHIImageBinding Image;
        internal readonly RHIBufferBinding Buffer;
        internal readonly bool HasUniform;
        internal readonly RHIUniformSnapshot? Uniform;
        internal ShaderInputValidation History;
        internal int NativeSampler;
        internal nint BufferIdentity;
        internal GpuBufferMetadata? BufferMetadata;
        internal UniformPublication.Candidate? CommittedUniform;

        /// <summary>Resolves immutable input selections once; native identity and availability remain execution-time facts.</summary>
        internal Input(GpuPreparedBindings.ActiveEntry active, RHIResourceBindings inputs)
        {
            Binding = active.Binding;
            inputs.TrySampler(active.Identity, active.Element, out Sampler);
            inputs.TryImage(active.Identity, out var image);
            bool layered = image.View.Layered;
            if (image.DefaultView)
            {
                var target = (TextureDimension)GpuPreparedBindings.CompatibleTextureTarget(Binding.Type);
                layered = target is TextureDimension.Three or TextureDimension.TwoArray or TextureDimension.Cube
                    or TextureDimension.OneArray or TextureDimension.CubeArray or TextureDimension.TwoMultisampleArray;
            }
            Image = image.View with { Layered = layered };
            inputs.TryBuffer(active.Identity, out Buffer);
            HasUniform = inputs.TryUniform(active.Identity, out var uniform);
            Uniform = uniform.Snapshot;
        }
    }

    #region Internal API
    internal readonly GpuPreparedBindings Interface;
    internal readonly Input[] Inputs;
    internal readonly Input[] Textures;
    internal readonly Input[] Other;
    internal readonly Input[] Uniforms;
    internal bool InputsChanged = true;
    internal bool Published;
    internal ulong PublicationRevision;

    /// <summary>Checks the complete supplied schema and partitions active work for reuse by immediate and recorded execution.</summary>
    internal OpenGLBindingPlan(GpuPreparedBindings prepared, RHIResourceBindings inputs)
    {
        Interface = prepared;
        foreach (var input in inputs.Samplers)
        {
            var entry = prepared.Resolve(input.Identity);
            if (entry.Contract.Name is not null
                && (entry.Contract.Kind != ShaderBindingKind.Sampler || input.Element >= entry.ActiveElements.Count))
                throw new ArgumentException("Sampler input does not match its prepared contract.");
        }
        foreach (var input in inputs.Images) RequireKind(prepared, input.Identity, ShaderBindingKind.Image);
        foreach (var input in inputs.Uniforms) RequireKind(prepared, input.Identity, ShaderBindingKind.UniformBlock);
        foreach (var input in inputs.Buffers)
            RequireKind(prepared, input.Identity, input.Role switch
            {
                BufferRole.Uniform => ShaderBindingKind.UniformBlock,
                BufferRole.ShaderStorage => ShaderBindingKind.StorageBlock,
                BufferRole.AtomicCounter => ShaderBindingKind.AtomicCounter,
                _ => throw new ArgumentOutOfRangeException(nameof(inputs))
            });
        var all = new List<Input>();
        var textures = new List<Input>();
        var other = new List<Input>();
        var uniforms = new List<Input>();
        foreach (var active in prepared.ActiveEntries)
        {
            var input = new Input(active, inputs);
            all.Add(input);
            if (input.Binding.Contract.Kind == ShaderBindingKind.Sampler) textures.Add(input);
            else other.Add(input);
            if (input.Binding.Contract.Kind == ShaderBindingKind.UniformBlock && input.HasUniform) uniforms.Add(input);
        }
        // Stable slot order belongs to preparation, not to each draw's texture publication.
        textures.Sort(static (left, right) => left.Binding.Contract.Binding.Slot.CompareTo(right.Binding.Contract.Binding.Slot));
        Inputs = all.ToArray(); Textures = textures.ToArray(); Other = other.ToArray(); Uniforms = uniforms.ToArray();
    }
    #endregion

    #region Private
    /// <summary>Allows excluded permutation properties while rejecting incompatible represented input kinds.</summary>
    private static void RequireKind(GpuPreparedBindings prepared, ulong identity, ShaderBindingKind kind)
    {
        var entry = prepared.Resolve(identity);
        if (entry.Contract.Name is not null && entry.Contract.Kind != kind)
            throw new ArgumentException("Resource input kind does not match its prepared contract.");
    }
    #endregion
}
