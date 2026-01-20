using System;
using System.Diagnostics;

using OpenTK.Graphics.OpenGL;

namespace VanillaGraphicsExpanded.Rendering;

/// <summary>
/// Fixed-size 3D texture / array texture wrapper.
/// Inherits streamed upload defaults from <see cref="GpuTexture"/>.
/// </summary>
public sealed class Texture3D : GpuTexture
{
    private Texture3D() { }

    public static Texture3D Create(
        int width,
        int height,
        int depth,
        PixelInternalFormat format,
        TextureFilterMode filter = TextureFilterMode.Linear,
        TextureTarget textureTarget = TextureTarget.Texture3D,
        string? debugName = null)
    {
        if (width <= 0)
        {
            Debug.WriteLine($"[Texture3D] Invalid width {width}, defaulting to 1");
            width = 1;
        }

        if (height <= 0)
        {
            Debug.WriteLine($"[Texture3D] Invalid height {height}, defaulting to 1");
            height = 1;
        }

        if (depth <= 0)
        {
            Debug.WriteLine($"[Texture3D] Invalid depth {depth}, defaulting to 1");
            depth = 1;
        }

        if (textureTarget != TextureTarget.Texture3D && textureTarget != TextureTarget.Texture2DArray)
        {
            Debug.WriteLine($"[Texture3D] Unsupported target {textureTarget}, defaulting to Texture3D");
            textureTarget = TextureTarget.Texture3D;
        }

        var texture = new Texture3D
        {
            width = width,
            height = height,
            depth = depth,
            internalFormat = format,
            filterMode = filter,
            textureTarget = textureTarget,
            debugName = debugName
        };

        texture.AllocateOrReallocate3DTexture();
        return texture;
    }
}
