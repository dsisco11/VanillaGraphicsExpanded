using System;
using OpenTK.Graphics.OpenGL;

namespace VanillaGraphicsExpanded.Rendering;

/// <summary>Exposes an externally owned texture through the typed texture contract without acquiring its storage.</summary>
/// <remarks>The borrowing owner must retire this metadata snapshot whenever external storage is replaced or reallocated.</remarks>
internal sealed class BorrowedTexture : GpuTexture
{
    #region Public API
    /// <summary>Captures target, dimensions, format and allocated mip levels without changing native state.</summary>
    internal BorrowedTexture(int externalTexture)
    {
        if (externalTexture <= 0 || !GL.IsTexture(externalTexture))
            throw new ArgumentOutOfRangeException(nameof(externalTexture));

        // Query the object directly: borrowing does not impose a sampler target or bind the image.
        GL.GetTextureParameter(externalTexture, GetTextureParameter.TextureTarget, out int target);
        textureTarget = (TextureTarget)target;
        GL.GetTextureLevelParameter(externalTexture, 0, GetTextureParameter.TextureWidth, out width);
        GL.GetTextureLevelParameter(externalTexture, 0, GetTextureParameter.TextureHeight, out height);
        GL.GetTextureLevelParameter(externalTexture, 0, GetTextureParameter.TextureDepth, out depth);
        GL.GetTextureLevelParameter(externalTexture, 0, GetTextureParameter.TextureInternalFormat, out int format);
        if (width <= 0) throw new ArgumentException("Texture has no base-level storage.", nameof(externalTexture));
        height = Math.Max(1, height);
        depth = Math.Max(1, depth);
        internalFormat = (PixelInternalFormat)format;

        // Array layers do not shrink along a mip chain; only spatial dimensions bound its length.
        int extent = textureTarget switch
        {
            TextureTarget.Texture1D or TextureTarget.Texture1DArray => width,
            TextureTarget.Texture3D => Math.Max(width, Math.Max(height, depth)),
            _ => Math.Max(width, height)
        };
        if (textureTarget is not (TextureTarget.TextureRectangle or TextureTarget.TextureBuffer
            or TextureTarget.Texture2DMultisample or TextureTarget.Texture2DMultisampleArray))
        {
            for (int level = 1; (extent >>= 1) > 0; level++)
            {
                GL.GetTextureLevelParameter(externalTexture, level, GetTextureParameter.TextureWidth, out int mipWidth);
                if (mipWidth <= 0) break;
                StorageMipLevels = level + 1;
            }
        }
        textureId = externalTexture;
    }

    /// <summary>Labels the managed borrower without modifying the external object.</summary>
    public override void SetDebugName(string? name) { debugName = name; }

    /// <summary>Rejects handle transfer because the external owner retains disposal authority.</summary>
    public override nint Detach() => throw new NotSupportedException("Borrowed textures cannot transfer ownership.");
    #endregion

    #region Protected API
    /// <summary>Retiring this wrapper never deletes the externally owned texture.</summary>
    protected override bool OwnsResource => false;
    #endregion
}
