using VanillaGraphicsExpanded.Rendering.RHI;
using System;

namespace VanillaGraphicsExpanded.Rendering;

/// <summary>Selects texture allocation and sampling policy before requesting native operations.</summary>
public abstract partial class GpuTexture
{
    #region Private
    /// <summary>Checks target-specific allocation policy before replacing existing native storage.</summary>
    private static void Validate2DAllocation(TextureDimension target, int width, int height, int mipLevels)
    {
        if (target is not (TextureDimension.Two or TextureDimension.Rectangle or TextureDimension.Cube))
            throw new InvalidOperationException($"Two-dimensional allocation is not supported for {target}.");
        if (target == TextureDimension.Rectangle && Math.Max(1, mipLevels) != 1)
            throw new InvalidOperationException("TextureRectangle does not support mipmaps.");
        if (target == TextureDimension.Cube && width != height)
            throw new InvalidOperationException("Cube texture faces must be square.");
    }


    /// <summary>Resolves wrapper sampling policy and delegates the complete allocation operation.</summary>
    private void Allocate2DStorage(TextureDimension target, TextureFormat format, int width, int height, int mipLevels)
    {
        var request = new GpuTextureInitialization { Target = target, InternalFormat = format,
            Width = width, Height = height, Depth = 1, Levels = Math.Max(1, mipLevels),
            AllocationMode = GpuTextureAllocationMode.Mutable, BaseLevel = 0, MaxLevel = Math.Max(1, mipLevels) - 1 };
        RHI.RHICommandContext.Current.AllocateTextureOwner(this, request, filterMode, debugName);
    }
    #endregion
}
