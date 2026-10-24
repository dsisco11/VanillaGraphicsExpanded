using System;
using OpenTK.Graphics.OpenGL;

namespace VanillaGraphicsExpanded.Rendering;

/// <summary>Owns texture image allocation and sampling policy while StateCache executes native commands.</summary>
public abstract partial class GpuTexture
{
    #region Private
    #region Image storage
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

    /// <summary>Groups all image and sampling commands before exposing the resulting storage snapshot.</summary>
    private void Allocate2DStorage(TextureTarget target, PixelInternalFormat format, int width, int height, int mipLevels)
    {
        mipLevels = Math.Max(1, mipLevels);
        using var binding = StateCache.Current.BindTextureScope(target, 0, textureId);
        using (var update = StateCache.Current.BeginTextureStorageUpdate(this, target))
        {
            Allocate2DStorageBound(target, format, width, height, mipLevels);
            Apply2DTextureObjectParamsBound(target, filterMode, mipLevels);
            update.Complete();
        }
#if DEBUG
        GlDebug.TryLabel(ObjectLabelIdentifier.Texture, textureId, debugName);
#endif
    }

    /// <summary>Allocates every two-dimensional image at each mip; cube targets select six individual face images.</summary>
    private void Allocate2DStorageBound(TextureTarget textureTarget, PixelInternalFormat internalFormat, int width, int height, int mipLevels)
    {
        int faces = textureTarget == TextureTarget.TextureCubeMap ? 6 : 1;
        PixelFormat pixels = TextureFormatHelper.GetPixelFormat(internalFormat);
        PixelType type = TextureFormatHelper.GetPixelType(internalFormat);
        for (int level = 0; level < mipLevels; level++)
        {
            int lw = Math.Max(1, width >> level);
            int lh = Math.Max(1, height >> level);
            for (int face = 0; face < faces; face++)
                StateCache.Current.TextureImage2D(this,
                    textureTarget == TextureTarget.TextureCubeMap ? TextureTarget.TextureCubeMapPositiveX + face : textureTarget,
                    level, internalFormat, lw, lh, 0, pixels, type, IntPtr.Zero);
        }
    }

    /// <summary>Allocates layered storage using explicit creation arguments.</summary>
    private void Allocate3DStorageBound(TextureTarget textureTarget, PixelInternalFormat internalFormat, int width, int height, int depth, bool immutableStorage)
    {
        if (immutableStorage)
        {
            // Reallocation already replaces the object name, so immutable allocation
            // supports the same resize lifecycle without mutating shared old storage.
            StateCache.Current.TextureStorage3D(this, (TextureTarget3d)textureTarget, 1, (SizedInternalFormat)internalFormat, width, height, depth);
            return;
        }
        StateCache.Current.TextureImage3D(this,
            textureTarget,
            level: 0,
            internalformat: internalFormat,
            width: width,
            height: height,
            depth: depth,
            border: 0,
            format: TextureFormatHelper.GetPixelFormat(internalFormat),
            type: TextureFormatHelper.GetPixelType(internalFormat),
            pixels: IntPtr.Zero);
    }

    #endregion

    #region Sampling policy
    /// <summary>Applies shared image sampling defaults while retaining legacy 2D mip policy and configurable cube filtering.</summary>
    private void Apply2DTextureObjectParamsBound(TextureTarget textureTarget, TextureFilterMode filterMode, int mipLevels)
    {
        // When a sampler object is bound to the unit, it overrides these parameters.
        // GpuTexture.Bind/TryBind explicitly unbinds the sampler to ensure these apply.

        TextureMinFilter min;
        TextureMagFilter mag;
        if (mipLevels <= 1)
        {
            min = filterMode == TextureFilterMode.Linear ? TextureMinFilter.Linear : TextureMinFilter.Nearest;
            mag = filterMode == TextureFilterMode.Linear ? TextureMagFilter.Linear : TextureMagFilter.Nearest;
        }
        else if (textureTarget == TextureTarget.TextureCubeMap)
        {
            // Cube resources retain the requested interpolation across their populated mip chain.
            min = filterMode == TextureFilterMode.Linear ? TextureMinFilter.LinearMipmapLinear : TextureMinFilter.NearestMipmapNearest;
            mag = filterMode == TextureFilterMode.Linear ? TextureMagFilter.Linear : TextureMagFilter.Nearest;
        }
        else
        {
            // Keep parity with the legacy sampler-object path.
            min = TextureMinFilter.NearestMipmapNearest;
            mag = TextureMagFilter.Nearest;
        }

        StateCache.Current.TextureParameter(this, textureTarget, TextureParameterName.TextureMinFilter, (int)min);
        StateCache.Current.TextureParameter(this, textureTarget, TextureParameterName.TextureMagFilter, (int)mag);

        StateCache.Current.TextureParameter(this, textureTarget, TextureParameterName.TextureWrapS, (int)TextureWrapMode.ClampToEdge);
        StateCache.Current.TextureParameter(this, textureTarget, TextureParameterName.TextureWrapT, (int)TextureWrapMode.ClampToEdge);
        if (textureTarget == TextureTarget.TextureCubeMap)
            StateCache.Current.TextureParameter(this, textureTarget, TextureParameterName.TextureWrapR, (int)TextureWrapMode.ClampToEdge);

        int maxLevel = Math.Max(0, mipLevels - 1);
        StateCache.Current.TextureParameter(this, textureTarget, TextureParameterName.TextureBaseLevel, 0);
        StateCache.Current.TextureParameter(this, textureTarget, TextureParameterName.TextureMaxLevel, maxLevel);
    }

    /// <summary>Applies the requested layered-texture sampling policy.</summary>
    private void Apply3DTextureObjectParamsBound(TextureTarget textureTarget, TextureFilterMode filterMode)
    {
        TextureMinFilter min = filterMode == TextureFilterMode.Linear ? TextureMinFilter.Linear : TextureMinFilter.Nearest;
        TextureMagFilter mag = filterMode == TextureFilterMode.Linear ? TextureMagFilter.Linear : TextureMagFilter.Nearest;

        StateCache.Current.TextureParameter(this, textureTarget, TextureParameterName.TextureMinFilter, (int)min);
        StateCache.Current.TextureParameter(this, textureTarget, TextureParameterName.TextureMagFilter, (int)mag);

        StateCache.Current.TextureParameter(this, textureTarget, TextureParameterName.TextureWrapS, (int)TextureWrapMode.ClampToEdge);
        StateCache.Current.TextureParameter(this, textureTarget, TextureParameterName.TextureWrapT, (int)TextureWrapMode.ClampToEdge);

        if (textureTarget == TextureTarget.Texture3D)
        {
            StateCache.Current.TextureParameter(this, textureTarget, TextureParameterName.TextureWrapR, (int)TextureWrapMode.ClampToEdge);
        }

        StateCache.Current.TextureParameter(this, textureTarget, TextureParameterName.TextureBaseLevel, 0);
        StateCache.Current.TextureParameter(this, textureTarget, TextureParameterName.TextureMaxLevel, 0);
    }

    #endregion
    #endregion
}
