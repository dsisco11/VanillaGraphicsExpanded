using System;
using OpenTK.Graphics.OpenGL;

namespace VanillaGraphicsExpanded.Rendering;

/// <summary>Executes complete texture allocation and parameter operations before publishing their result.</summary>
internal sealed partial class StateCache
{
    #region Internal API
    /// <summary>Allocates all images and applies caller-selected sampling policy as one operation.</summary>
    internal void AllocateTexture2D(GpuTexture texture, TextureTarget target, PixelInternalFormat format,
        int width, int height, int levels, TextureMinFilter min, TextureMagFilter mag,
        TextureWrapMode wrap, int baseLevel, int maxLevel)
    {
        using var operation = new GpuResourceManager.MetadataOperation<GpuTextureMetadata>(texture, GpuMetadataChange.Storage);
        using var binding = BindTextureScope(target, 0, texture.TextureId);
        var pixels = TextureFormatHelper.GetPixelFormat(format);
        var type = TextureFormatHelper.GetPixelType(format);
        int faces = target == TextureTarget.TextureCubeMap ? 6 : 1;
        for (int level = 0; level < levels; level++)
            for (int face = 0; face < faces; face++)
                TextureImage2D(texture, target == TextureTarget.TextureCubeMap ? TextureTarget.TextureCubeMapPositiveX + face : target,
                    level, format, Math.Max(1, width >> level), Math.Max(1, height >> level), 0, pixels, type, IntPtr.Zero, operation);
        ConfigureTextureSampling(texture, target, min, mag, wrap, baseLevel, maxLevel, operation);
        RecoverTextureOperation(texture, target, operation);
    }

    /// <summary>Allocates layered storage and sampling settings under one original identity.</summary>
    internal void AllocateTexture3D(GpuTexture texture, TextureTarget target, PixelInternalFormat format,
        int width, int height, int depth, bool immutable, TextureMinFilter min, TextureMagFilter mag,
        TextureWrapMode wrap, int baseLevel, int maxLevel)
    {
        using var operation = new GpuResourceManager.MetadataOperation<GpuTextureMetadata>(texture, GpuMetadataChange.Storage);
        using var binding = BindTextureScope(target, 0, texture.TextureId);
        operation.RequireParticipation(texture, GpuMetadataChange.Storage);
        if (immutable) GL.TexStorage3D((TextureTarget3d)target, 1, (SizedInternalFormat)format, width, height, depth);
        else GL.TexImage3D(target, 0, format, width, height, depth, 0,
            TextureFormatHelper.GetPixelFormat(format), TextureFormatHelper.GetPixelType(format), IntPtr.Zero);
        ConfigureTextureSampling(texture, target, min, mag, wrap, baseLevel, maxLevel, operation);
        RecoverTextureOperation(texture, target, operation);
    }

    /// <summary>Changes both mip limits, preserving best-effort dispatch and observable recovery failures.</summary>
    internal void SetTextureMipRange(GpuTexture texture, TextureTarget target, int baseLevel, int maxLevel)
    {
        using var operation = new GpuResourceManager.MetadataOperation<GpuTextureMetadata>(texture, GpuMetadataChange.Metadata);
        using var binding = BindTextureScope(target, 0, texture.TextureId);
        try
        {
            TextureParameter(texture, target, TextureParameterName.TextureBaseLevel, baseLevel, operation);
            TextureParameter(texture, target, TextureParameterName.TextureMaxLevel, maxLevel, operation);
        }
        catch { return; }
        RecoverTextureOperation(texture, target, operation);
    }

    /// <summary>Defines an image as an explicitly participating step, or a standalone operation.</summary>
    internal void TextureImage2D(GpuTexture texture, TextureTarget target, int level, PixelInternalFormat format,
        int width, int height, int border, PixelFormat pixels, PixelType type, IntPtr data,
        GpuResourceManager.MetadataOperation<GpuTextureMetadata>? operation = null)
    {
        if (operation is null) RequireResourceOperationAvailable(texture.Kind, texture.ResourceId);
        else operation.RequireParticipation(texture, GpuMetadataChange.Storage);
        try
        {
            GL.TexImage2D(target, level, format, width, height, border, pixels, type, data);
            if (operation is null) GpuResourceManager.ObserveWrapperMetadata(texture,
                new GpuTextureMetadataReader(target >= TextureTarget.TextureCubeMapPositiveX && target <= TextureTarget.TextureCubeMapNegativeZ
                    ? TextureTarget.TextureCubeMap : target), GpuMetadataChange.Storage);
        }
        catch { operation?.Interrupt(); throw; }
    }

    /// <summary>Defines layered storage independently and recovers its complete metadata.</summary>
    internal void TextureImage3D(GpuTexture texture, TextureTarget target, int level, PixelInternalFormat internalformat,
        int width, int height, int depth, int border, PixelFormat format, PixelType type, IntPtr pixels)
    {
        RequireResourceOperationAvailable(texture.Kind, texture.ResourceId);
        GL.TexImage3D(target, level, internalformat, width, height, depth, border, format, type, pixels);
        GpuResourceManager.ObserveWrapperMetadata(texture, new GpuTextureMetadataReader(target), GpuMetadataChange.Storage);
    }

    /// <summary>Allocates immutable layered storage independently and recovers its metadata.</summary>
    internal void TextureStorage3D(GpuTexture texture, TextureTarget3d target, int levels, SizedInternalFormat format,
        int width, int height, int depth)
    {
        RequireResourceOperationAvailable(texture.Kind, texture.ResourceId);
        GL.TexStorage3D(target, levels, format, width, height, depth);
        GpuResourceManager.ObserveWrapperMetadata(texture, new GpuTextureMetadataReader((TextureTarget)target), GpuMetadataChange.Storage);
    }

    /// <summary>Applies one parameter and publishes only when called as an independent operation.</summary>
    internal void TextureParameter(GpuTexture texture, TextureTarget target, TextureParameterName name, int value,
        GpuResourceManager.MetadataOperation<GpuTextureMetadata>? operation = null)
    {
        if (operation is null) RequireResourceOperationAvailable(texture.Kind, texture.ResourceId);
        else operation.RequireParticipation(texture, GpuMetadataChange.Metadata);
        try
        {
            GL.TexParameter(target, name, value);
            if (operation is null && name is TextureParameterName.TextureBaseLevel or TextureParameterName.TextureMaxLevel)
                GpuResourceManager.ObserveWrapperMetadata(texture, new GpuTextureMetadataReader(target), GpuMetadataChange.Metadata);
        }
        catch { operation?.Interrupt(); throw; }
    }
    #endregion

    #region Private
    /// <summary>Executes sampling choices supplied by the wrapper without publishing intermediate parameter state.</summary>
    private void ConfigureTextureSampling(GpuTexture texture, TextureTarget target, TextureMinFilter min,
        TextureMagFilter mag, TextureWrapMode wrap, int baseLevel, int maxLevel, GpuResourceManager.MetadataOperation<GpuTextureMetadata> operation)
    {
        TextureParameter(texture, target, TextureParameterName.TextureMinFilter, (int)min, operation);
        TextureParameter(texture, target, TextureParameterName.TextureMagFilter, (int)mag, operation);
        TextureParameter(texture, target, TextureParameterName.TextureWrapS, (int)wrap, operation);
        TextureParameter(texture, target, TextureParameterName.TextureWrapT, (int)wrap, operation);
        if (target is TextureTarget.Texture3D or TextureTarget.TextureCubeMap)
            TextureParameter(texture, target, TextureParameterName.TextureWrapR, (int)wrap, operation);
        TextureParameter(texture, target, TextureParameterName.TextureBaseLevel, baseLevel, operation);
        TextureParameter(texture, target, TextureParameterName.TextureMaxLevel, maxLevel, operation);
    }

    /// <summary>Explicitly reads the completed allocation until texture commands supply complete derived candidates.</summary>
    private static void RecoverTextureOperation(GpuTexture texture, TextureTarget target,
        GpuResourceManager.MetadataOperation<GpuTextureMetadata> operation)
    {
        operation.RequireLive();
        var candidate = GpuResourceManager.ReadTextureOperationResult(texture, target);
        operation.Commit(candidate);
    }
    #endregion
}
