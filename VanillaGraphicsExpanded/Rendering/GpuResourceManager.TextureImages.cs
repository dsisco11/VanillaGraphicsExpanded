using VanillaGraphicsExpanded.Rendering.RHI;
using System.Collections.Immutable;

namespace VanillaGraphicsExpanded.Rendering;

/// <summary>Recovers only image descriptors affected by an external definition or mip-generation command.</summary>
internal sealed partial class GpuResourceManager
{
    #region Internal API
    /// <summary>Retains unaffected images and parameters while acquiring an inclusive range of changed levels.</summary>
    internal bool RefreshTextureImages(GpuResourceMetadataEntry<GpuTextureMetadata> entry,
        int firstLevel, int lastLevel, CubeFace face, bool generation, bool wasAvailable)
    {
        RequireMetadataContext();
        RequireLiveEntry(entry);
        if (entry.OperationInProgress) throw new System.InvalidOperationException("An operation owns this resource result.");
        BeginMetadataCompletion(entry, generation ? GpuMetadataChange.None : GpuMetadataChange.Storage);
        entry.BeginRefresh(generation ? GpuMetadataChange.None : GpuMetadataChange.Storage);
        bool finished = false;
        bool changed = !generation;
        try
        {
            var images = entry.Metadata.Levels.ToBuilder();
            int faces = entry.Metadata.Target == TextureDimension.Cube && face == 0 ? 6 : 1;
            for (int level = firstLevel; level <= lastLevel; level++)
                for (int index = 0; index < faces; index++)
                {
                    CubeFace selected = faces == 6 ? CubeFace.PositiveX + index : face;
                    if (!GpuTextureImageReader.TryRead((int)entry.Id, entry.Metadata.Target, selected, level, out var image)) return false;
                    changed |= ReplaceImage(images, image);
                }
            RequireMetadataContext();
            RequireLiveEntry(entry);
            RequireUninterruptedCompletion(entry);
            entry.Publish(entry.Metadata with { Levels = images.ToImmutable() }, changed ? GpuMetadataChange.Storage : GpuMetadataChange.None);
            finished = true;
            return true;
        }
        finally
        {
            entry.CompletionInProgress = false;
            if (changed || !wasAvailable || !finished) entry.NotifyImageDependents(suppressErrors: !finished);
        }
    }
    #endregion

    #region Private
    /// <summary>Updates one image in place and reports whether its allocation descriptor changed.</summary>
    private static bool ReplaceImage(ImmutableArray<GpuTextureLevelMetadata>.Builder images, GpuTextureLevelMetadata candidate)
    {
        for (int i = 0; i < images.Count; i++)
            if (images[i].Level == candidate.Level && images[i].Face == candidate.Face)
            {
                bool changed = images[i] != candidate;
                if (candidate.Allocated) images[i] = candidate;
                else images.RemoveAt(i);
                return changed;
            }
        if (!candidate.Allocated) return false;
        images.Add(candidate);
        return true;
    }
    #endregion
}
