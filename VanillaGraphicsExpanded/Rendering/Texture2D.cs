using System;
using System.Diagnostics;

using OpenTK.Graphics.OpenGL;

namespace VanillaGraphicsExpanded.Rendering;

/// <summary>
/// Fixed-size 2D texture wrapper.
/// Inherits streamed upload defaults from <see cref="GpuTexture"/>.
/// </summary>
public sealed class Texture2D : GpuTexture
{
    private Texture2D() { }

    public static Texture2D Create(
        int width,
        int height,
        PixelInternalFormat format,
        TextureFilterMode filter = TextureFilterMode.Nearest,
        string? debugName = null)
    {
        if (width <= 0)
        {
            Debug.WriteLine($"[Texture2D] Invalid width {width}, defaulting to 1");
            width = 1;
        }

        if (height <= 0)
        {
            Debug.WriteLine($"[Texture2D] Invalid height {height}, defaulting to 1");
            height = 1;
        }

        var texture = new Texture2D
        {
            width = width,
            height = height,
            depth = 1,
            internalFormat = format,
            textureTarget = TextureTarget.Texture2D,
            filterMode = filter,
            debugName = debugName
        };

        texture.AllocateOrReallocate2DTexture(mipLevels: 1);
        return texture;
    }

    public static Texture2D CreateWithData(
        int width,
        int height,
        PixelInternalFormat format,
        float[] data,
        TextureFilterMode filter = TextureFilterMode.Nearest,
        string? debugName = null)
    {
        var texture = Create(width, height, format, filter, debugName);
        texture.UploadData(data);
        return texture;
    }

    public static Texture2D CreateWithDataImmediate(
        int width,
        int height,
        PixelInternalFormat format,
        float[] data,
        TextureFilterMode filter = TextureFilterMode.Nearest,
        string? debugName = null)
    {
        var texture = Create(width, height, format, filter, debugName);
        texture.UploadDataImmediate(data);
        return texture;
    }

}
