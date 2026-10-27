using VanillaGraphicsExpanded.Rendering.RHI;
using System;
using OpenTK.Graphics.OpenGL;

namespace VanillaGraphicsExpanded.Rendering;

/// <summary>Projects selected framebuffer images from existing canonical texture or renderbuffer entries.</summary>
internal sealed partial class GpuResourceManager
{
    #region Internal API
    /// <summary>Associates an image selection with the same backing registration without another native query.</summary>
    internal void AssociateImageResource(GpuResourceRegistration entry, GpuFramebufferAttachment resource)
    {
        ArgumentNullException.ThrowIfNull(resource);
        RequireMetadataContext();
        RequireLiveEntry(entry);
        ValidateAssociation(entry, resource);
        if (entry is not GpuResourceMetadataEntry<GpuTextureMetadata> && entry is not GpuResourceMetadataEntry<GpuRenderbufferMetadata>)
            throw new ArgumentException("An image association requires texture or renderbuffer storage.", nameof(entry));
        entry.AssociateImage(resource);
    }

    /// <summary>Assigns a selected image from canonical backing facts without storing a per-wrapper delegate.</summary>
    internal bool AssignImageMetadata(GpuResourceRegistration entry, GpuResource resource)
    {
        if (resource is not GpuFramebufferAttachment image) return false;
        var selection = image.MetadataImageSelection;
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
        if (data.Target == TextureDimension.Buffer || data.Levels.IsDefault || selection.Level < 0 || selection.Layer < 0) return null;
        if (data.Target == TextureDimension.Cube && selection.Layered)
            return ProjectLayeredCubeImage(data, selection);
        foreach (var level in data.Levels)
        {
            if (level.Level != selection.Level || !level.Allocated || level.Width <= 0
                || level.Height <= 0 || level.Depth <= 0) continue;
            if (data.Target == TextureDimension.Cube && level.Face != selection.Face) continue;
            if (data.Target != TextureDimension.Cube && selection.Face != 0) return null;
            bool oneDimensionalArray = data.Target == TextureDimension.OneArray;
            bool hasLayers = oneDimensionalArray || data.Target is TextureDimension.Three
                or TextureDimension.TwoArray or TextureDimension.CubeArray or TextureDimension.TwoMultisampleArray;
            int layers = oneDimensionalArray ? level.Height : level.Depth;
            if (hasLayers ? selection.Layer >= layers : selection.Layer != 0 || selection.Layered) return null;
            if (selection.ViewCount != 0)
            {
                if (data.Target != TextureDimension.TwoArray || !selection.Layered || selection.BaseView < 0
                    || selection.ViewCount < 1 || (long)selection.BaseView + selection.ViewCount > layers) return null;
                return new(level.Width, level.Height, selection.ViewCount, level.Format, level.Samples);
            }
            return new(level.Width, oneDimensionalArray ? 1 : level.Height,
                hasLayers && selection.Layered ? layers : 1, level.Format, level.Samples);
        }
        return null;
    }

    /// <summary>Requires six compatible allocated faces before exposing a whole layered cube image.</summary>
    private static GpuImageMetadata? ProjectLayeredCubeImage(GpuTextureMetadata data, GpuImageSelection selection)
    {
        if (selection.Layer != 0 || selection.Face != 0) return null;
        GpuImageMetadata? image = null;
        for (int face = 0; face < 6; face++)
        {
            var selected = ProjectTextureImage(data, new(selection.Level, 0,
                CubeFace.PositiveX + face, false));
            if (!selected.HasValue || image.HasValue && image.Value != selected.Value) return null;
            image = selected;
        }
        return image.HasValue ? image.Value with { Depth = 6 } : null;
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
