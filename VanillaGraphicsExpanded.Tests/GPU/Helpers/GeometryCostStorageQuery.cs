using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.Rendering;

namespace VanillaGraphicsExpanded.Tests.GPU.Helpers;

/// <summary>Measures allocated texture dimensions and formats instead of inferring residency from requested coverage.</summary>
internal static class GeometryCostStorageQuery
{
    /// <summary>Returns nominal texel bytes from actual GL level allocation; driver metadata/compression are excluded.</summary>
    public static long TextureBytes(int texture, TextureTarget target)
    {
        using var binding = GlStateCache.Current.BindTextureScope(target, 0, texture);
        GL.GetTexLevelParameter(target, 0, GetTextureParameter.TextureWidth, out int width);
        GL.GetTexLevelParameter(target, 0, GetTextureParameter.TextureHeight, out int height);
        GL.GetTexLevelParameter(target, 0, GetTextureParameter.TextureDepth, out int depth);
        GL.GetTexLevelParameter(target, 0, GetTextureParameter.TextureInternalFormat, out int format);
        int bytes = (PixelInternalFormat)format switch
        {
            PixelInternalFormat.R8ui => 1, PixelInternalFormat.R16f => 2,
            PixelInternalFormat.R32ui or PixelInternalFormat.Rgba8 => 4,
            PixelInternalFormat.Rgba16f => 8, PixelInternalFormat.Rgba32ui => 16,
            _ => throw new InvalidOperationException($"Unexpected measured texture format {format}")
        };
        return (long)width * Math.Max(height, 1) * Math.Max(depth, 1) * bytes;
    }
}
