using VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;
using VanillaGraphicsExpanded.Rendering.RHI;
using System;
using System.Diagnostics;

using OpenTK.Graphics.OpenGL;

namespace VanillaGraphicsExpanded.Rendering;

/// <summary>
/// RAII wrapper around an OpenGL sampler object.
/// Deletion is deferred to <see cref="GpuResourceManager"/> when available.
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
#if DEBUG
        if (samplerId != 0)
        {
            GlDebug.TryLabel(ObjectLabelIdentifier.Sampler, samplerId, debugName);
        }
#endif
    }

    /// <summary>
    /// Binds this sampler to a texture unit via <c>glBindSampler</c>.
    /// </summary>
    public void Bind(int unit)
    {
        if (!IsValid)
        {
            Debug.WriteLine("[GpuSampler] Attempted to bind disposed or invalid sampler");
            return;
        }

        OpenGLImageBindings.BindSampler(StateCache.Current, unit, samplerId);
    }

    /// <summary>
    /// Attempts to bind this sampler to a texture unit. Returns <c>false</c> if invalid.
    /// </summary>
    public bool TryBind(int unit)
    {
        if (!IsValid)
        {
            return false;
        }

        OpenGLImageBindings.BindSampler(StateCache.Current, unit, samplerId);
        return true;
    }

    /// <summary>
    /// Unbinds any sampler from the given texture unit (binds sampler 0).
    /// </summary>
    public static void Unbind(int unit)
    {
        OpenGLImageBindings.UnbindSampler(StateCache.Current, unit);
    }

    /// <summary>
    /// Binds this sampler and returns a scope that restores the previous sampler binding for the unit when disposed.
    /// </summary>
    public BindingScope BindScope(int unit)
    {
        ObjectDisposedException.ThrowIf(!IsValid, this);
        var scope = StateCache.Current.BindSamplerScope(unit, samplerId);
        return new BindingScope(scope);
    }

    /// <summary>Applies owner-selected minification and magnification through the semantic backend.</summary>
    public void SetFilter(RHISamplerFilter minFilter, RHISamplerFilter magFilter)
    {
        if (!IsValid) return;
        RHICommandContext.Current.ConfigureSampler(this, new RHISamplerDescription { MinFilter = minFilter, MagFilter = magFilter });
    }

    /// <summary>Applies only the requested wrap axes without reading sampler parameters.</summary>
    public void SetWrap(RHISamplerAddress wrapS, RHISamplerAddress wrapT, RHISamplerAddress? wrapR = null)
    {
        if (!IsValid) return;
        var request = new RHISamplerDescription { AddressS = wrapS, AddressT = wrapT };
        if (wrapR.HasValue) request.AddressR = wrapR.Value;
        RHICommandContext.Current.ConfigureSampler(this, request);
    }

    /// <summary>Applies owner-selected shadow comparison without swallowing native dispatch failures.</summary>
    public void SetCompare(bool enabled, RHIComparison comparison)
    {
        if (!IsValid) return;
        RHICommandContext.Current.ConfigureSampler(this, new RHISamplerDescription { Compare = enabled, Comparison = comparison });
    }

    /// <summary>
    /// Scope that restores the previous sampler binding for a unit when disposed.
    /// </summary>
    public readonly struct BindingScope : IDisposable
    {
        private readonly StateCache.SamplerScope scope;

        internal BindingScope(StateCache.SamplerScope scope)
        {
            this.scope = scope;
        }

        public void Dispose()
        {
            scope.Dispose();
        }
    }
}
