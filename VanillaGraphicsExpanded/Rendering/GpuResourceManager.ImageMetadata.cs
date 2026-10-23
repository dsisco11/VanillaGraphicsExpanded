using System;
using OpenTK.Graphics.OpenGL;

namespace VanillaGraphicsExpanded.Rendering;

/// <summary>Projects selected framebuffer images from existing canonical texture or renderbuffer entries.</summary>
internal sealed partial class GpuResourceManager
{
    #region Internal API
    /// <summary>Associates an image selection with the same backing registration without another native query.</summary>
    internal void AssociateImageResource(GpuResourceRegistration entry, GpuResource<GpuImageMetadata> resource,
        GpuImageSelection selection)
    {
        ArgumentNullException.ThrowIfNull(resource);
        RequireMetadataContext();
        RequireLiveEntry(entry);
        ValidateAssociation(entry, resource);
        if (entry is not GpuResourceMetadataEntry<GpuTextureMetadata> && entry is not GpuResourceMetadataEntry<GpuRenderbufferMetadata>)
            throw new ArgumentException("An image association requires texture or renderbuffer storage.", nameof(entry));
        resource.MetadataImageSelection = selection;
        entry.AssociateImage(resource);
    }

    /// <summary>Assigns a selected image from canonical backing facts without storing a per-wrapper delegate.</summary>
    internal bool AssignImageMetadata(GpuResourceRegistration entry, GpuResource resource)
    {
        if (resource is not GpuResource<GpuImageMetadata> image || image.MetadataImageSelection is not { } selection) return false;
        GpuImageMetadata? candidate = entry switch
        {
            GpuResourceMetadataEntry<GpuTextureMetadata> texture => ProjectTextureImage(texture.Metadata, selection),
            GpuResourceMetadataEntry<GpuRenderbufferMetadata> renderbuffer => ProjectRenderbufferImage(renderbuffer.Metadata, selection),
            _ => null
        };
        if (!candidate.HasValue) return false;
        image.HandleMetadataChanged(candidate.Value);
        return true;
    }
    #endregion
    #region Private
    /// <summary>Selects a represented mip, cube face or layer; unsupported or absent images stay unavailable.</summary>
    private static GpuImageMetadata? ProjectTextureImage(GpuTextureMetadata data, GpuImageSelection selection)
    {
        if (data.Levels.IsDefault || selection.Level < 0 || selection.Layer < 0) return null;
        foreach (var level in data.Levels)
        {
            if (level.Level != selection.Level || !level.Allocated || level.Width <= 0
                || level.Height <= 0 || level.Depth <= 0) continue;
            if (data.Target == TextureTarget.TextureCubeMap && level.Face != selection.Face) continue;
            if (data.Target != TextureTarget.TextureCubeMap && selection.Face != 0) return null;
            bool oneDimensionalArray = data.Target == TextureTarget.Texture1DArray;
            bool hasLayers = oneDimensionalArray || data.Target is TextureTarget.Texture3D
                or TextureTarget.Texture2DArray or TextureTarget.TextureCubeMapArray or TextureTarget.Texture2DMultisampleArray;
            int layers = oneDimensionalArray ? level.Height : level.Depth;
            if (hasLayers ? selection.Layer >= layers : selection.Layer != 0 || selection.Layered) return null;
            return new(level.Width, oneDimensionalArray ? 1 : level.Height,
                hasLayers && selection.Layered ? layers : 1, level.Format, level.Samples);
        }
        return null;
    }

    /// <summary>Projects a renderbuffer only for the single native image it actually represents.</summary>
    private static GpuImageMetadata? ProjectRenderbufferImage(GpuRenderbufferMetadata data, GpuImageSelection selection)
    {
        if (selection.Level != 0 || selection.Layer != 0 || selection.Face != 0 || selection.Layered
            || data.Width <= 0 || data.Height <= 0) return null;
        return new(data.Width, data.Height, 1, data.Format, data.Samples);
    }
    #endregion
}
