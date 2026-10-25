using System;
using OpenTK.Graphics.OpenGL;

namespace VanillaGraphicsExpanded.Rendering;

/// <summary>Captures actual renderbuffer storage while preserving the caller's binding.</summary>
internal sealed class GpuRenderbufferMetadataReader : IGpuResourceMetadataReader<GpuRenderbufferMetadata>
{
    internal static readonly GpuRenderbufferMetadataReader Instance = new();

    #region Internal API
    /// <summary>Reads only the allocation result that cannot be resolved without sample capabilities.</summary>
    internal static int ReadSamples(int id)
    {
        int samples;
        if (GpuSupport.Graphics.SupportsArbDirectStateAccess)
            GL.GetNamedRenderbufferParameter(id, RenderbufferParameterName.RenderbufferSamples, out samples);
        else
        {
            using var binding = StateCache.Current.BindRenderbufferScope(id);
            GL.GetRenderbufferParameter(RenderbufferTarget.Renderbuffer, RenderbufferParameterName.RenderbufferSamples, out samples);
        }
        if (samples < 0) throw new InvalidOperationException("Renderbuffer sample output is unavailable.");
        return samples;
    }
    #endregion

    #region Public API
    /// <inheritdoc />
    public bool IsResource(nint id) => id != 0 && id >= int.MinValue && id <= uint.MaxValue && GL.IsRenderbuffer(unchecked((int)id));

    /// <inheritdoc />
    public bool TryRead(nint id, out GpuRenderbufferMetadata metadata)
    {
        metadata = default;
        if (!IsResource(id)) return false;
        int width, height, format, samples;
        if (GpuSupport.Graphics.SupportsArbDirectStateAccess)
        {
            int name = unchecked((int)id);
            GL.GetNamedRenderbufferParameter(name, RenderbufferParameterName.RenderbufferWidth, out width);
            GL.GetNamedRenderbufferParameter(name, RenderbufferParameterName.RenderbufferHeight, out height);
            GL.GetNamedRenderbufferParameter(name, RenderbufferParameterName.RenderbufferInternalFormat, out format);
            GL.GetNamedRenderbufferParameter(name, RenderbufferParameterName.RenderbufferSamples, out samples);
        }
        else
        {
            using var scope = StateCache.Current.BindRenderbufferScope(unchecked((int)id));
            GL.GetRenderbufferParameter(RenderbufferTarget.Renderbuffer, RenderbufferParameterName.RenderbufferWidth, out width);
            GL.GetRenderbufferParameter(RenderbufferTarget.Renderbuffer, RenderbufferParameterName.RenderbufferHeight, out height);
            GL.GetRenderbufferParameter(RenderbufferTarget.Renderbuffer, RenderbufferParameterName.RenderbufferInternalFormat, out format);
            GL.GetRenderbufferParameter(RenderbufferTarget.Renderbuffer, RenderbufferParameterName.RenderbufferSamples, out samples);
        }
        if (width < 0 || height < 0 || format <= 0 || samples < 0) return false;
        metadata = new(width, height, (PixelInternalFormat)format, samples);
        return true;
    }
    #endregion
}
