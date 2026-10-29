using VanillaGraphicsExpanded.Rendering.RHI;
using System;
using System.Diagnostics;


namespace VanillaGraphicsExpanded.Rendering;

/// <summary>
/// RAII wrapper around an OpenGL sampler object.
/// Deletion is deferred to <see cref="RHIResourceManager"/> when available.
/// All methods require a current GL context on the calling thread.
/// </summary>
public sealed class GpuSampler : GpuResource<GpuSamplerMetadata>, IDisposable
{
    private int samplerId;

    /// <summary>Supplies a query-free consumer reference without lifecycle authority.</summary>
    public static implicit operator GpuSamplerRef(GpuSampler? value) => new(value);

    /// <inheritdoc />
    public override nint ResourceId
    {
        get => samplerId;
        protected set => samplerId = (int)value;
    }

    protected override GpuResourceKind ResourceKind => GpuResourceKind.Sampler;

    /// <summary>
    /// Gets the underlying OpenGL sampler id.
    /// </summary>
    public int SamplerId => samplerId;

    /// <summary>
    /// Returns <c>true</c> when the sampler has a non-zero id and has not been disposed.
    /// </summary>
    public new bool IsValid => base.IsValid;

    /// <summary>Declares an unpublished wrapper for manager-owned creation.</summary>
    internal GpuSampler() { }


    /// <summary>
    /// Sets the debug label for this sampler (debug builds only).
    /// </summary>
    public override void SetDebugName(string? debugName)
    {
        RHICommandContext.TryLabelResource(this, debugName);
    }


    /// <summary>Applies owner-selected minification and magnification through the semantic backend.</summary>
    public void SetFilter(SamplerFilter minFilter, SamplerFilter magFilter)
    {
        if (!IsValid) return;
        RHICommandContext.Current.ConfigureSampler(this, new RHISamplerDescription { MinFilter = minFilter, MagFilter = magFilter });
    }

    /// <summary>Applies only the requested wrap axes without reading sampler parameters.</summary>
    public void SetWrap(SamplerAddressMode wrapS, SamplerAddressMode wrapT, SamplerAddressMode? wrapR = null)
    {
        if (!IsValid) return;
        var request = new RHISamplerDescription { AddressS = wrapS, AddressT = wrapT };
        if (wrapR.HasValue) request.AddressR = wrapR.Value;
        RHICommandContext.Current.ConfigureSampler(this, request);
    }

    /// <summary>Applies owner-selected shadow comparison without swallowing native dispatch failures.</summary>
    public void SetCompare(bool enabled, ComparisonFunction comparison)
    {
        if (!IsValid) return;
        RHICommandContext.Current.ConfigureSampler(this, new RHISamplerDescription { Compare = enabled, Comparison = comparison });
    }


}
