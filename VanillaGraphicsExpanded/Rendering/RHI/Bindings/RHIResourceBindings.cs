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
        // These keys describe authored inputs only; resolving current resources belongs to execution.
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
    }
    #endregion

    #region Internal API
    /// <summary>Retains only successful compatibility history; it does not change the captured desired inputs.</summary>
    internal Dictionary<(ulong Identity, int Element), ShaderInputValidation> ValidationHistory { get; } = new();
    /// <summary>Records successful fixed-interface shape validation without changing captured input values.</summary>
    internal bool SchemaValidated { get; set; }
    /// <summary>Looks up one frozen array element without re-enumerating caller input.</summary>
    internal bool TrySampler(ulong identity, int element, out RHISamplerBinding value) => samplers.TryGetValue((identity, element), out value);
    /// <summary>Looks up an explicit or default frozen image view.</summary>
    internal bool TryImage(ulong identity, out RHIImageInput value) => images.TryGetValue(identity, out value);
    /// <summary>Looks up a retained buffer selection without querying its current capacity.</summary>
    internal bool TryBuffer(ulong identity, out RHIBufferBinding value) => buffers.TryGetValue(identity, out value);
    /// <summary>Looks up a captured uniform version independently of later CPU edits.</summary>
    internal bool TryUniform(ulong identity, out RHIUniformBinding value) => uniforms.TryGetValue(identity, out value);
    #endregion
}
