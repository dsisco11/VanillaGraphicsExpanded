using System;
using OpenTK.Graphics.OpenGL;

namespace VanillaGraphicsExpanded.Rendering;

/// <summary>Executes individual texture commands and publishes their native metadata through the manager.</summary>
internal sealed partial class StateCache
{
    #region Internal API
    #region Update groups
    /// <summary>Defers publication while the texture owner allocates its image and sampling configuration.</summary>
    internal MetadataUpdate<GpuTextureMetadata> BeginTextureStorageUpdate(GpuTexture texture, TextureTarget target = 0)
        => new(this, texture, new GpuTextureMetadataReader(target == 0 ? texture.TextureTarget : target), GpuMetadataChange.Storage);

    /// <summary>Defers publication while the texture owner changes its represented parameter selection.</summary>
    internal MetadataUpdate<GpuTextureMetadata> BeginTextureMetadataUpdate(GpuTexture texture)
        => new(this, texture, new GpuTextureMetadataReader(texture.TextureTarget), GpuMetadataChange.Metadata);

    #endregion

    #region Native commands
    /// <summary>Defines one bound texture image, including a selected cube face.</summary>
    internal void TextureImage2D(GpuTexture texture, TextureTarget target, int level, PixelInternalFormat format,
        int width, int height, int border, PixelFormat pixels, PixelType type, IntPtr data)
    {
        GL.TexImage2D(target, level, format, width, height, border, pixels, type, data);
        ObserveMetadata(texture, new GpuTextureMetadataReader(target >= TextureTarget.TextureCubeMapPositiveX
            && target <= TextureTarget.TextureCubeMapNegativeZ ? TextureTarget.TextureCubeMap : target), GpuMetadataChange.Storage);
    }

    /// <summary>Defines one bound layered texture image.</summary>
    internal void TextureImage3D(GpuTexture texture, TextureTarget target, int level, PixelInternalFormat internalformat,
        int width, int height, int depth, int border, PixelFormat format, PixelType type, IntPtr pixels)
    {
        GL.TexImage3D(target, level, internalformat, width, height, depth, border, format, type, pixels);
        ObserveMetadata(texture, new GpuTextureMetadataReader(target), GpuMetadataChange.Storage);
    }

    /// <summary>Allocates immutable layered storage for a bound texture.</summary>
    internal void TextureStorage3D(GpuTexture texture, TextureTarget3d target, int levels, SizedInternalFormat format,
        int width, int height, int depth)
    {
        GL.TexStorage3D(target, levels, format, width, height, depth);
        ObserveMetadata(texture, new GpuTextureMetadataReader((TextureTarget)target), GpuMetadataChange.Storage);
    }

    /// <summary>Sets one texture parameter; only represented metadata parameters require snapshot refresh.</summary>
    internal void TextureParameter(GpuTexture texture, TextureTarget target, TextureParameterName name, int value)
    {
        GL.TexParameter(target, name, value);
        if (name is TextureParameterName.TextureBaseLevel or TextureParameterName.TextureMaxLevel)
            ObserveMetadata(texture, new GpuTextureMetadataReader(target), GpuMetadataChange.Metadata);
    }
    #endregion
    #endregion
}
