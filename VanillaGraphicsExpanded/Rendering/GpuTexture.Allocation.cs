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
        var request = new GpuTextureInitialization { Target = target, InternalFormat = format,
            Width = width, Height = height, Depth = 1, Levels = Math.Max(1, mipLevels),
            AllocationMode = GpuTextureAllocationMode.Mutable, BaseLevel = 0, MaxLevel = Math.Max(1, mipLevels) - 1 };
        RHI.RHICommandContext.Current.AllocateTextureOwner(this, request, filterMode, debugName);
    }
    #endregion
}
