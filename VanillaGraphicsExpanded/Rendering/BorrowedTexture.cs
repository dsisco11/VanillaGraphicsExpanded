using System;
using OpenTK.Graphics.OpenGL;

namespace VanillaGraphicsExpanded.Rendering;

/// <summary>Exposes an externally owned texture through the typed texture contract without acquiring its storage.</summary>
/// <remarks>Unobserved external storage mutations require explicit refresh; deletion observed by StateCache retires the common registration.</remarks>
internal sealed class BorrowedTexture : GpuTexture
{
    #region Public API
    /// <summary>Captures target, dimensions, format and allocated mip levels without changing native state.</summary>
    internal BorrowedTexture(int externalTexture)
    {
        if (externalTexture <= 0 || !GL.IsTexture(externalTexture))
            throw new ArgumentOutOfRangeException(nameof(externalTexture));

        textureId = externalTexture;
        GpuResourceManager.RecoverWrapperMetadata(this, GpuTextureMetadataReader.Instance);

        if (width <= 0 || height <= 0)
            throw new ArgumentException("Texture has no allocated base-level image.", nameof(externalTexture));
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
