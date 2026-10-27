using System;
using OpenTK.Graphics.OpenGL;

namespace VanillaGraphicsExpanded.Rendering;

/// <summary>Selects texture allocation and sampling policy before requesting native operations.</summary>
public abstract partial class GpuTexture
{
    #region Private
    /// <summary>Checks target-specific allocation policy before replacing existing native storage.</summary>
    private static void Validate2DAllocation(TextureTarget target, int width, int height, int mipLevels)
    {
        if (target is not (TextureTarget.Texture2D or TextureTarget.TextureRectangle or TextureTarget.TextureCubeMap))
            throw new InvalidOperationException($"Two-dimensional allocation is not supported for {target}.");
        if (target == TextureTarget.TextureRectangle && Math.Max(1, mipLevels) != 1)
            throw new InvalidOperationException("TextureRectangle does not support mipmaps.");
        if (target == TextureTarget.TextureCubeMap && width != height)
            throw new InvalidOperationException("Cube texture faces must be square.");
    }


    /// <summary>Resolves wrapper sampling policy and delegates the complete allocation operation.</summary>
    private void Allocate2DStorage(TextureTarget target, PixelInternalFormat format, int width, int height, int mipLevels)
    {
        var request = new RHI.RHITextureDescription
        {
            Dimension = RHI.Backends.OpenGL.OpenGLImageValues.Dimension(target), Format = RHI.Backends.OpenGL.OpenGLImageValues.Format(format),
            Width = width, Height = height, MipLevels = Math.Max(1, mipLevels), Storage = RHI.ResourceStorageMode.Mutable,
            Filter = filterMode, BaseLevel = 0, MaxLevel = Math.Max(1, mipLevels) - 1, Label = debugName
        };
        if (textureId == 0) RHI.RHICommandContext.Current.OpenGL.InitializeTextureOwner(this, request);
        else RHI.RHICommandContext.Current.AllocateTextureStorage(this, request);
    }
    #endregion
}
