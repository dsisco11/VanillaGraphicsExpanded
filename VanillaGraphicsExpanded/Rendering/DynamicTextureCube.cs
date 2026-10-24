using System;
using OpenTK.Graphics.OpenGL;

namespace VanillaGraphicsExpanded.Rendering;

/// <summary>Owns six square color texture faces with configurable format, filtering and mip storage.</summary>
/// <remarks>Mip allocation does not populate or generate mip contents. Renderers own content generation
/// and can attach individual faces through <see cref="GpuFramebufferAttachment.FromTexture(GpuTexture, int, int?, TextureTarget?)"/>.</remarks>
public sealed class DynamicTextureCube : GpuTexture
{
    #region Public API
    /// <summary>Returns the number of allocated mip levels, independent of the active sampling range.</summary>
    public int MipLevels => StorageMipLevels;

    /// <summary>Allocates all six faces on the current context, restoring bindings and releasing partial allocation on failure.</summary>
    /// <param name="size">Width and height of each face.</param>
    /// <param name="format">Color storage format, independent of the resource's contents.</param>
    /// <param name="mipLevels">Number of allocated levels, from one to the complete chain length.</param>
    /// <param name="filter">Nearest or linear filtering; mipmapped storage uses the matching mip filter.</param>
    /// <param name="debugName">Optional resource label.</param>
    public static DynamicTextureCube Create(int size, PixelInternalFormat format, int mipLevels = 1,
        TextureFilterMode filter = TextureFilterMode.Nearest, string? debugName = null)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(size);
        int maximumLevels = 1 + (int)MathF.Floor(MathF.Log2(size));
        if (mipLevels < 1 || mipLevels > maximumLevels) throw new ArgumentOutOfRangeException(nameof(mipLevels));
        if (!Enum.IsDefined(filter)) throw new ArgumentOutOfRangeException(nameof(filter));
        if (TextureFormatHelper.IsDepthFormat(format) || TextureFormatHelper.IsStencilFormat(format))
            throw new ArgumentException("Cube color storage does not accept depth or stencil formats.", nameof(format));
        PixelFormat pixels = TextureFormatHelper.GetPixelFormat(format);
        bool integer = pixels is PixelFormat.RedInteger or PixelFormat.RgInteger or PixelFormat.RgbInteger or PixelFormat.RgbaInteger;
        if (integer && filter != TextureFilterMode.Nearest)
            throw new ArgumentException("Integer textures require nearest filtering.", nameof(filter));
        int limit = GpuSupport.MaxCubeMapTextureSize;
        if (size > limit) throw new ArgumentOutOfRangeException(nameof(size), "Face size exceeds the current context's cube limit.");
        return new DynamicTextureCube(size, format, mipLevels, filter, debugName);
    }
    #endregion

    #region Private
    /// <summary>Allocates cube storage from validated requests and receives its metadata through manager publication.</summary>
    private DynamicTextureCube(int size, PixelInternalFormat format, int mipLevels,
        TextureFilterMode filter, string? debugName)
    {
        filterMode = filter;
        this.debugName = debugName;
        // Allocation publishes observed metadata; constructor arguments remain command inputs.
        try
        {
            AllocateOrReallocate2DTexture(size, size, format, mipLevels, TextureTarget.TextureCubeMap);
            if (GL.GetError() != ErrorCode.NoError) throw new InvalidOperationException("Cube texture allocation failed.");
        }
        catch { Dispose(); throw; }
    }
    #endregion
}
