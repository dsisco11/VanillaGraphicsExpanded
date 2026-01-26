using System;
using System.Diagnostics;

using OpenTK.Graphics.OpenGL;

namespace VanillaGraphicsExpanded.Rendering;

/// <summary>
/// RAII wrapper around an OpenGL sampler object.
/// Deletion is deferred to <see cref="GpuResourceManager"/> when available.
/// All methods require a current GL context on the calling thread.
/// </summary>
public sealed class GpuSampler : GpuResource, IDisposable
{
    private int samplerId;

    protected override nint ResourceId
    {
        get => samplerId;
        set => samplerId = (int)value;
    }

    protected override GpuResourceKind ResourceKind => GpuResourceKind.Sampler;

    /// <summary>
    /// Gets the underlying OpenGL sampler id.
    /// </summary>
    public int SamplerId => samplerId;

    /// <summary>
    /// Returns <c>true</c> when the sampler has a non-zero id and has not been disposed.
    /// </summary>
    public new bool IsValid => samplerId != 0 && !IsDisposed;

    private GpuSampler(int samplerId)
    {
        this.samplerId = samplerId;
    }

    /// <summary>
    /// Creates a new sampler object.
    /// </summary>
    public static GpuSampler Create(string? debugName = null)
    {
        int id = GL.GenSampler();
        if (id == 0)
        {
            throw new InvalidOperationException("glGenSamplers failed.");
        }

        var sampler = new GpuSampler(id);
        sampler.SetDebugName(debugName);
        return sampler;
    }

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

        GlStateCache.Current.BindSampler(unit, samplerId);
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

        GlStateCache.Current.BindSampler(unit, samplerId);
        return true;
    }

    /// <summary>
    /// Unbinds any sampler from the given texture unit (binds sampler 0).
    /// </summary>
    public static void Unbind(int unit)
    {
        GlStateCache.Current.UnbindSampler(unit);
    }

    /// <summary>
    /// Binds this sampler and returns a scope that restores the previous sampler binding for the unit when disposed.
    /// </summary>
    public BindingScope BindScope(int unit)
    {
        var scope = GlStateCache.Current.BindSamplerScope(unit, samplerId);
        return new BindingScope(scope);
    }

    /// <summary>
    /// Sets min/mag filtering parameters.
    /// </summary>
    public void SetFilter(TextureMinFilter minFilter, TextureMagFilter magFilter)
    {
        if (!IsValid)
        {
            return;
        }

        try
        {
            GL.SamplerParameter(samplerId, SamplerParameterName.TextureMinFilter, (int)minFilter);
            GL.SamplerParameter(samplerId, SamplerParameterName.TextureMagFilter, (int)magFilter);
        }
        catch
        {
        }
    }

    /// <summary>
    /// Sets wrap mode parameters for S/T (and optionally R).
    /// </summary>
    public void SetWrap(TextureWrapMode wrapS, TextureWrapMode wrapT, TextureWrapMode? wrapR = null)
    {
        if (!IsValid)
        {
            return;
        }

        try
        {
            GL.SamplerParameter(samplerId, SamplerParameterName.TextureWrapS, (int)wrapS);
            GL.SamplerParameter(samplerId, SamplerParameterName.TextureWrapT, (int)wrapT);
            if (wrapR.HasValue)
            {
                GL.SamplerParameter(samplerId, SamplerParameterName.TextureWrapR, (int)wrapR.Value);
            }
        }
        catch
        {
        }
    }

    /// <summary>
    /// Sets depth comparison sampling parameters (useful for shadow samplers).
    /// </summary>
    public void SetCompare(TextureCompareMode mode, DepthFunction function)
    {
        if (!IsValid)
        {
            return;
        }

        try
        {
            GL.SamplerParameter(samplerId, SamplerParameterName.TextureCompareMode, (int)mode);
            GL.SamplerParameter(samplerId, SamplerParameterName.TextureCompareFunc, (int)function);
        }
        catch
        {
        }
    }

    /// <summary>
    /// Scope that restores the previous sampler binding for a unit when disposed.
    /// </summary>
    public readonly struct BindingScope : IDisposable
    {
        private readonly GlStateCache.SamplerScope scope;

        internal BindingScope(GlStateCache.SamplerScope scope)
        {
            this.scope = scope;
        }

        public void Dispose()
        {
            scope.Dispose();
        }
    }
}
