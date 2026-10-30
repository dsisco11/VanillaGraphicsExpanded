using System;
using VanillaGraphicsExpanded.Rendering.Pipeline.Descriptions;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.Rendering.RHI;

namespace VanillaGraphicsExpanded.Rendering.Pipeline.Passes;

/// <summary>Checks framebuffer feedback against published subresource ranges without native queries.</summary>
internal sealed partial class RenderPass
{
    #region Internal API
    /// <summary>Checks accessible mips against the selected draw state; the caller owns prior-write synchronization.</summary>
    internal static void ValidateSampledTexture(GpuTextureRef texture, DepthStencilDesc? depthStencil = null)
    {
        if (active is null) return;
        var data = texture.RequireMetadata();
        foreach (var level in data.Levels)
        {
            if (!level.Allocated || level.Level < data.BaseLevel || level.Level > data.MaxLevel) continue;
            ValidateTextureRange(texture, data, level.Level, null, depthStencil);
        }
    }

    /// <summary>Checks the explicit image mip and layer; image access ignores sampler base/max parameters.</summary>
    internal static void ValidateImageTexture(RHIImageBinding image)
    {
        if (active is null) return;
        ValidateTextureRange(image.Texture, image.Texture.RequireMetadata(), image.Level,
            image.Layered ? null : image.Layer);
    }
    #endregion

    #region Private
    /// <summary>Compares object-relative selections on the same name and absolute selections on proven shared storage.</summary>
    private static void ValidateTextureRange(GpuTextureRef texture, GpuTextureMetadata data, int level, int? layer, DepthStencilDesc? depthStencil = null)
    {
        int name = texture.TextureId;
        var view = data.View;
        int storage = RHIResourceManager.TextureStorageName(texture.Resource);
        foreach (var pair in active!.description.Target.AttachmentImages)
        {
            var attached = pair.Value;
            if (attached.TextureId == 0) continue;
            var attachedView = RHIResourceManager.TextureStorageSelection(attached.Resource ?? attached);
            int attachedStorage = RHIResourceManager.TextureStorageName(attached.Resource ?? attached);
            bool sameObject = attached.TextureId == name;
            if (!sameObject && storage != attachedStorage) continue;

            // Canonical view offsets are already cumulative for nested views. Add them once
            // only when comparing different objects sharing that proven original allocation.
            int selectedLevel = level + (sameObject ? 0 : view?.BaseLevel ?? 0);
            int attachedLevel = attached.MipLevel + (sameObject ? 0 : attachedView?.BaseLevel ?? 0);
            if (selectedLevel != attachedLevel) continue;
            long first = (sameObject ? 0 : view?.BaseLayer ?? 0) + (layer ?? 0);
            long count = layer.HasValue ? 1 : data.Target == TextureDimension.Three
                ? int.MaxValue : data.View?.LayerCount ?? int.MaxValue;
            int? attachedLayer = attached.Layer ?? (attached.CubeFace is { } face
                ? (int)face - (int)CubeFace.PositiveX : null);
            long attachedFirst = (sameObject ? 0 : attachedView?.BaseLayer ?? 0)
                + (attached.ViewCount > 0 ? attached.BaseView : attachedLayer ?? 0);
            long attachedCount = attached.ViewCount > 0 ? attached.ViewCount : attachedLayer.HasValue
                ? 1 : attachedView?.LayerCount ?? int.MaxValue;
            if (first >= attachedFirst + attachedCount || attachedFirst >= first + count) continue;
            // OpenGL 4.5 permits attached depth sampling when this draw cannot write it.
            // Color, stencil and image-load/store overlap remain conservative. Pass callers
            // must explicitly order earlier writes when their execution sequence requires it.
            if (pair.Key == FramebufferSlot.Depth && depthStencil is not null
                && (!depthStencil.DepthTest || !depthStencil.DepthWrite))
            {
                continue;
            }
            throw new InvalidOperationException($"Texture {name} access overlaps writable or unqualified framebuffer attachment {pair.Key}.");
        }
    }
    #endregion
}
