using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;

namespace VanillaGraphicsExpanded.Rendering.RHI;

/// <summary>Freezes one complete desired resource set for an exact executable generation without inspecting native state.</summary>
public sealed class RHIResourceBindings
{
    private readonly Dictionary<(ulong Identity, int Element), RHISamplerBinding> samplers;
    private readonly Dictionary<ulong, RHIImageInput> images;
    private readonly Dictionary<ulong, RHIBufferBinding> buffers;
    private readonly Dictionary<ulong, RHIUniformBinding> uniforms;
    internal RHIResourceBindingStructure Structure { get; }
    private bool uniformsPrepared;

    #region Public API
    /// <summary>Gets the exact prepared-interface generation; captured tables never follow asset replacement.</summary>
    public RHIShaderRef Shader { get; }
    /// <summary>Gets copied sampler selections, including explicit array elements.</summary>
    public ImmutableArray<RHISamplerBinding> Samplers { get; }
    /// <summary>Gets copied image view selections.</summary>
    public ImmutableArray<RHIImageInput> Images { get; }
    /// <summary>Gets copied uniform/storage/counter buffer selections.</summary>
    public ImmutableArray<RHIBufferBinding> Buffers { get; }
    /// <summary>Gets immutable logical uniform versions, never ring offsets reserved during capture.</summary>
    public ImmutableArray<RHIUniformBinding> Uniforms { get; }
    /// <summary>Copies supplied shape and rejects duplicate or contradictory input selections without resource queries.</summary>
    public RHIResourceBindings(RHIShaderRef shader, IEnumerable<RHISamplerBinding>? samplers = null,
        IEnumerable<RHIImageInput>? images = null, IEnumerable<RHIBufferBinding>? buffers = null,
        IEnumerable<RHIUniformBinding>? uniforms = null)
    {
        if (shader.IsEmpty) throw new ArgumentException("A fixed shader reference is required.", nameof(shader));
        Shader = shader;
        Samplers = samplers?.ToImmutableArray() ?? ImmutableArray<RHISamplerBinding>.Empty;
        Images = images?.ToImmutableArray() ?? ImmutableArray<RHIImageInput>.Empty;
        Buffers = buffers?.ToImmutableArray() ?? ImmutableArray<RHIBufferBinding>.Empty;
        Uniforms = uniforms?.ToImmutableArray() ?? ImmutableArray<RHIUniformBinding>.Empty;
        // These keys describe authored inputs only; canonical compatibility belongs to the retained preparation.
        this.samplers = Samplers.ToDictionary(value => (value.Identity, value.Element));
        this.images = Images.ToDictionary(value => value.Identity);
        this.buffers = Buffers.ToDictionary(value => value.Identity);
        this.uniforms = Uniforms.ToDictionary(value => value.Identity);
        foreach (var sampler in Samplers)
            if (sampler.Element < 0) throw new ArgumentOutOfRangeException(nameof(samplers));
        foreach (var image in Images)
            if (image.View.Level < 0 || image.View.Layer < 0 || !Enum.IsDefined(image.View.Access)
                || image.View.Format is { } format && (!Enum.IsDefined(format) || format == TextureFormat.Undefined))
                throw new ArgumentException("Invalid supplied image selection.", nameof(images));
        foreach (var buffer in Buffers)
            if (buffer.Role is not (BufferRole.Uniform or BufferRole.ShaderStorage or BufferRole.AtomicCounter)
                || buffer.Whole && (buffer.Offset != 0 || buffer.Length != 0)
                || !buffer.Whole && (buffer.Offset < 0 || buffer.Length <= 0)
                || this.uniforms.ContainsKey(buffer.Identity))
                throw new ArgumentException("Invalid or duplicate uniform/storage/counter selection.", nameof(buffers));
        Structure = new(this);
    }

    /// <summary>Captures new logical uniform versions while reusing this owner's unchanged resource structure.</summary>
    public RHIResourceBindings WithUniforms(IEnumerable<RHIUniformBinding> values) => new(this, values);
    #endregion

    #region Internal API
    /// <summary>Checks each captured block's immutable shape once, independently of structural resource refresh.</summary>
    internal void Prepare(RHICapabilities capabilities, IRHIContextLifetime context)
    {
        Structure.Prepare(capabilities, context);
        if (uniformsPrepared) return;
        foreach (var input in Structure.Inputs)
        {
            if (!input.HasUniform) continue;
            uniforms.TryGetValue(input.Identity, out var uniform);
            if (uniform.Snapshot is not { } snapshot)
            {
                if (input.Binding.Contract.Binding.Required) throw new InvalidOperationException("A required uniform block is absent.");
            }
            else if (snapshot.Bytes.Length < input.Binding.MinimumSize || snapshot.Bytes.Length > capabilities.MaxUniformBlockSize)
                throw new ArgumentException("Captured uniform bytes do not match the prepared block.");
        }
        uniformsPrepared = true;
    }
    /// <summary>Looks up one frozen array element without re-enumerating caller input.</summary>
    internal bool TrySampler(ulong identity, int element, out RHISamplerBinding value) => samplers.TryGetValue((identity, element), out value);
    /// <summary>Looks up an explicit or default frozen image view.</summary>
    internal bool TryImage(ulong identity, out RHIImageInput value) => images.TryGetValue(identity, out value);
    /// <summary>Looks up a retained buffer selection without querying its current capacity.</summary>
    internal bool TryBuffer(ulong identity, out RHIBufferBinding value) => buffers.TryGetValue(identity, out value);
    /// <summary>Looks up a captured uniform version independently of later CPU edits.</summary>
    internal bool TryUniform(ulong identity, out RHIUniformBinding value) => uniforms.TryGetValue(identity, out value);
    #endregion

    #region Private
    /// <summary>Copies only logical uniform versions; resource declarations and preparation remain shared.</summary>
    private RHIResourceBindings(RHIResourceBindings source, IEnumerable<RHIUniformBinding> values)
    {
        Shader = source.Shader;
        Samplers = source.Samplers; Images = source.Images; Buffers = source.Buffers;
        samplers = source.samplers; images = source.images; buffers = source.buffers;
        Uniforms = values.ToImmutableArray();
        uniforms = Uniforms.ToDictionary(value => value.Identity);
        if (uniforms.Count != source.uniforms.Count || uniforms.Keys.Any(key => !source.uniforms.ContainsKey(key)))
            throw new ArgumentException("Uniform versions must preserve the resource declaration.", nameof(values));
        Structure = source.Structure;
    }
    #endregion
}
