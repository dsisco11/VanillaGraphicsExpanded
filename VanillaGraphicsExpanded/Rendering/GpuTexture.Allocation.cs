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
        mipLevels = Math.Max(1, mipLevels);
        var mag = filterMode == TextureFilterMode.Linear ? TextureMagFilter.Linear : TextureMagFilter.Nearest;
        var min = mipLevels <= 1
            ? (filterMode == TextureFilterMode.Linear ? TextureMinFilter.Linear : TextureMinFilter.Nearest)
            : target == TextureTarget.TextureCubeMap && filterMode == TextureFilterMode.Linear
                ? TextureMinFilter.LinearMipmapLinear : TextureMinFilter.NearestMipmapNearest;
        if (mipLevels > 1 && target != TextureTarget.TextureCubeMap) mag = TextureMagFilter.Nearest;
        if (textureId == 0)
        {
            var request = new GpuTextureInitialization
            {
                Target = target, AllocationMode = GpuTextureAllocationMode.Mutable, InternalFormat = format,
                Width = width, Height = height, Levels = mipLevels, BaseLevel = 0, MaxLevel = mipLevels - 1
            };
            if (!GpuResourceManager.CreateTexture(request, this, out _).HasValue)
                throw new InvalidOperationException("Texture creation failed.");
            StateCache.Current.ConfigureTextureSampling(this, target, min, mag, TextureWrapMode.ClampToEdge);
        }
        else StateCache.Current.AllocateTexture2D(this, target, format, width, height, mipLevels, min, mag,
            TextureWrapMode.ClampToEdge, 0, mipLevels - 1);
#if DEBUG
        GlDebug.TryLabel(ObjectLabelIdentifier.Texture, textureId, debugName);
#endif
    }
    #endregion
}
