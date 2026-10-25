using System;
using OpenTK.Graphics.OpenGL;

namespace VanillaGraphicsExpanded.Rendering;

/// <summary>Executes complete texture allocation and parameter operations before publishing their result.</summary>
internal sealed partial class StateCache
{
    #region Internal API
    /// <summary>Applies wrapper-selected sampling policy without rereading or changing image metadata.</summary>
    internal void ConfigureTextureSampling(GpuTexture texture, TextureTarget target, TextureMinFilter min,
        TextureMagFilter mag, TextureWrapMode wrap)
    {
        RequireResourceOperationAvailable(texture.Kind, texture.ResourceId);
        using var binding = BindTextureScope(target, 0, texture.TextureId);
        GL.TexParameter(target, TextureParameterName.TextureMinFilter, (int)min);
        GL.TexParameter(target, TextureParameterName.TextureMagFilter, (int)mag);
        GL.TexParameter(target, TextureParameterName.TextureWrapS, (int)wrap);
        GL.TexParameter(target, TextureParameterName.TextureWrapT, (int)wrap);
        if (target is TextureTarget.Texture3D or TextureTarget.TextureCubeMap)
            GL.TexParameter(target, TextureParameterName.TextureWrapR, (int)wrap);
    }

    /// <summary>Allocates all images and applies caller-selected sampling policy as one operation.</summary>
    internal void AllocateTexture2D(GpuTexture texture, TextureTarget target, PixelInternalFormat format,
        int width, int height, int levels, TextureMinFilter min, TextureMagFilter mag,
        TextureWrapMode wrap, int baseLevel, int maxLevel)
    {
        var request = new GpuTextureInitialization
        {
            Target = target, AllocationMode = GpuTextureAllocationMode.Mutable, InternalFormat = format,
            Width = width, Height = height, Levels = levels, BaseLevel = baseLevel, MaxLevel = maxLevel
        }.Resolve();
        var previous = GpuResourceManager.AcquireTextureCommandInput(texture);
        if (previous.ImmutableStorage) throw new InvalidOperationException("Immutable storage cannot be redefined.");
        using var operation = new GpuResourceManager.MetadataOperation<GpuTextureMetadata>(texture, GpuMetadataChange.Storage);
        using var binding = BindTextureScope(target, 0, texture.TextureId);
        var candidate = AllocateTextureImages(request);
        // Mutable redefinition leaves previously allocated higher levels intact.
        var images = candidate.Levels.ToBuilder();
        foreach (var image in previous.Levels)
            if (image.Level >= levels) images.Add(image);
        ConfigureTextureSampling(texture, target, min, mag, wrap, baseLevel, maxLevel, operation);
        operation.Commit(candidate with { Levels = images.ToImmutable() });
    }

    /// <summary>Changes both mip limits, preserving best-effort dispatch and observable recovery failures.</summary>
    internal void SetTextureMipRange(GpuTexture texture, TextureTarget target, int baseLevel, int maxLevel)
    {
        if (baseLevel < 0 || maxLevel < 0 || target == TextureTarget.TextureRectangle && baseLevel != 0)
            throw new ArgumentOutOfRangeException(nameof(baseLevel));
        var previous = GpuResourceManager.AcquireTextureCommandInput(texture);
        using var operation = new GpuResourceManager.MetadataOperation<GpuTextureMetadata>(texture, GpuMetadataChange.Metadata);
        using var binding = BindTextureScope(target, 0, texture.TextureId);
        try
        {
            TextureParameter(texture, target, TextureParameterName.TextureBaseLevel, baseLevel, operation);
            TextureParameter(texture, target, TextureParameterName.TextureMaxLevel, maxLevel, operation);
        }
        catch { return; }
        operation.Commit(previous with { BaseLevel = baseLevel, MaxLevel = maxLevel });
    }

    /// <summary>Defines an image as an explicitly participating step, or a standalone operation.</summary>
    internal void TextureImage2D(GpuTexture texture, TextureTarget target, int level, PixelInternalFormat format,
        int width, int height, int border, PixelFormat pixels, PixelType type, IntPtr data,
        GpuResourceManager.MetadataOperation<GpuTextureMetadata>? operation = null)
    {
        if (operation is not null)
        {
            operation.RequireParticipation(texture, GpuMetadataChange.Storage);
            try { GL.TexImage2D(target, level, format, width, height, border, pixels, type, data); }
            catch { operation.Interrupt(); throw; }
            return;
        }
        var previous = GpuResourceManager.AcquireTextureCommandInput(texture);
        if (previous.ImmutableStorage) throw new InvalidOperationException("Immutable storage cannot be redefined.");
        using var update = new GpuResourceManager.MetadataOperation<GpuTextureMetadata>(texture, GpuMetadataChange.Storage);
        GL.TexImage2D(target, level, format, width, height, border, pixels, type, data);
        format = GpuTextureFormatReader.ResolveBoundFormat(target, level, format);
        var face = previous.Target == TextureTarget.TextureCubeMap ? target : 0;
        update.Commit(ReplaceTextureImage(previous, new(level, face, width, height, 1, format, 0, width > 0 && height > 0)));
    }

    /// <summary>Defines layered storage independently and recovers its complete metadata.</summary>
    internal void TextureImage3D(GpuTexture texture, TextureTarget target, int level, PixelInternalFormat internalformat,
        int width, int height, int depth, int border, PixelFormat format, PixelType type, IntPtr pixels)
    {
        var previous = GpuResourceManager.AcquireTextureCommandInput(texture);
        if (previous.ImmutableStorage) throw new InvalidOperationException("Immutable storage cannot be redefined.");
        using var operation = new GpuResourceManager.MetadataOperation<GpuTextureMetadata>(texture, GpuMetadataChange.Storage);
        GL.TexImage3D(target, level, internalformat, width, height, depth, border, format, type, pixels);
        internalformat = GpuTextureFormatReader.ResolveBoundFormat(target, level, internalformat);
        operation.Commit(ReplaceTextureImage(previous, new(level, 0, width, height, depth, internalformat, 0,
            width > 0 && height > 0 && depth > 0)));
    }

    /// <summary>Applies one parameter and publishes only when called as an independent operation.</summary>
    internal void TextureParameter(GpuTexture texture, TextureTarget target, TextureParameterName name, int value,
        GpuResourceManager.MetadataOperation<GpuTextureMetadata>? operation = null)
    {
        if (operation is not null)
        {
            operation.RequireParticipation(texture, GpuMetadataChange.Metadata);
            try { GL.TexParameter(target, name, value); }
            catch { operation.Interrupt(); throw; }
            return;
        }
        RequireResourceOperationAvailable(texture.Kind, texture.ResourceId);
        if (name is not (TextureParameterName.TextureBaseLevel or TextureParameterName.TextureMaxLevel))
        {
            GL.TexParameter(target, name, value);
            return;
        }
        if (value < 0 || target == TextureTarget.TextureRectangle && name == TextureParameterName.TextureBaseLevel && value != 0)
            throw new ArgumentOutOfRangeException(nameof(value));
        var previous = GpuResourceManager.AcquireTextureCommandInput(texture);
        using var update = new GpuResourceManager.MetadataOperation<GpuTextureMetadata>(texture, GpuMetadataChange.Metadata);
        GL.TexParameter(target, name, value);
        update.Commit(name == TextureParameterName.TextureBaseLevel ? previous with { BaseLevel = value } : previous with { MaxLevel = value });
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

    /// <summary>Replaces only the selected image while retaining unrelated allocated faces and levels.</summary>
    private static GpuTextureMetadata ReplaceTextureImage(GpuTextureMetadata previous, GpuTextureLevelMetadata image)
    {
        var levels = previous.Levels.ToBuilder();
        for (int i = levels.Count - 1; i >= 0; i--)
            if (levels[i].Level == image.Level && levels[i].Face == image.Face) levels.RemoveAt(i);
        if (image.Allocated) levels.Add(image);
        return previous with { Levels = levels.ToImmutable() };
    }
    #endregion
}
