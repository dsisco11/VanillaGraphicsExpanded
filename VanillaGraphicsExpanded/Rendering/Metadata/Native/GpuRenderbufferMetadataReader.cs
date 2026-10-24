using System;
using OpenTK.Graphics.OpenGL;

namespace VanillaGraphicsExpanded.Rendering;

/// <summary>Captures actual renderbuffer storage while preserving the caller's binding.</summary>
internal sealed class GpuRenderbufferMetadataReader : IGpuResourceMetadataReader<GpuRenderbufferMetadata>
{
    internal static readonly GpuRenderbufferMetadataReader Instance = new();

    #region Public API
    /// <inheritdoc />
    public bool IsResource(nint id) => id != 0 && id >= int.MinValue && id <= uint.MaxValue && GL.IsRenderbuffer(unchecked((int)id));

    /// <inheritdoc />
    public bool TryRead(nint id, out GpuRenderbufferMetadata metadata)
    {
        metadata = default;
        if (!IsResource(id)) return false;
        using var scope = StateCache.Current.BindRenderbufferScope(unchecked((int)id));
        GL.GetRenderbufferParameter(RenderbufferTarget.Renderbuffer, RenderbufferParameterName.RenderbufferWidth, out int width);
        GL.GetRenderbufferParameter(RenderbufferTarget.Renderbuffer, RenderbufferParameterName.RenderbufferHeight, out int height);
        GL.GetRenderbufferParameter(RenderbufferTarget.Renderbuffer, RenderbufferParameterName.RenderbufferInternalFormat, out int format);
        GL.GetRenderbufferParameter(RenderbufferTarget.Renderbuffer, RenderbufferParameterName.RenderbufferSamples, out int samples);
        if (width < 0 || height < 0 || format <= 0 || samples < 0) return false;
        metadata = new(width, height, (PixelInternalFormat)format, samples);
        return true;
    }
    #endregion
}
