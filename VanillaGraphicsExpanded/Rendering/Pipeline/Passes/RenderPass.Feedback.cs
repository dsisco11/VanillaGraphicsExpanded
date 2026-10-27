using System;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.Rendering.RHI;

namespace VanillaGraphicsExpanded.Rendering.Pipeline.Passes;

/// <summary>Checks framebuffer feedback against published subresource ranges without native queries.</summary>
internal sealed partial class RenderPass
{
    #region Internal API
    /// <summary>Checks all allocated mips accessible through the sampler's object-relative base/max range.</summary>
    internal static void ValidateSampledTexture(GpuTextureRef texture)
    {
        if (active is null) return;
        var data = texture.RequireMetadata();
        foreach (var level in data.Levels)
        {
            if (!level.Allocated || level.Level < data.BaseLevel || level.Level > data.MaxLevel) continue;
            ValidateTextureRange(texture, data, level.Level, null);
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
    private static void ValidateTextureRange(GpuTextureRef texture, GpuTextureMetadata data, int level, int? layer)
    {
        int name = texture.TextureId;
        var view = texture.Resource as GpuTextureView;
        int storage = view is null ? name : GpuResourceManager.TextureViewStorageName(view);
        foreach (var pair in active!.description.Target.AttachmentImages)
        {
            var attached = pair.Value;
            if (attached.TextureId == 0) continue;
            var attachedView = attached.Resource as GpuTextureView;
            int attachedStorage = attachedView is null ? attached.TextureId
                : GpuResourceManager.TextureViewStorageName(attachedView);
            bool sameObject = attached.TextureId == name;
            if (!sameObject && storage != attachedStorage) continue;

            // Canonical view offsets are already cumulative for nested views. Add them once
            // only when comparing different objects sharing that proven original allocation.
            int selectedLevel = level + (sameObject ? 0 : view?.BaseLevel ?? 0);
            int attachedLevel = attached.MipLevel + (sameObject ? 0 : attachedView?.BaseLevel ?? 0);
            if (selectedLevel != attachedLevel) continue;
            long first = (sameObject ? 0 : view?.BaseLayer ?? 0) + (layer ?? 0);
            long count = layer.HasValue ? 1 : data.Target == TextureTarget.Texture3D
                ? int.MaxValue : data.View?.LayerCount ?? int.MaxValue;
            int? attachedLayer = attached.Layer ?? (attached.CubeFace is { } face
                ? (int)face - (int)TextureTarget.TextureCubeMapPositiveX : null);
            long attachedFirst = (sameObject ? 0 : attachedView?.BaseLayer ?? 0)
                + (attached.ViewCount > 0 ? attached.BaseView : attachedLayer ?? 0);
            long attachedCount = attached.ViewCount > 0 ? attached.ViewCount : attachedLayer.HasValue
                ? 1 : attachedView?.LayerCount ?? int.MaxValue;
            if (first < attachedFirst + attachedCount && attachedFirst < first + count)
                throw new InvalidOperationException("Texture access overlaps an active framebuffer image.");
        }
    }
    #endregion
}
