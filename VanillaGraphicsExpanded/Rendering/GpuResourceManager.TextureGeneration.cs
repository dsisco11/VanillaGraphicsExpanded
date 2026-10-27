using VanillaGraphicsExpanded.Rendering.RHI;
using System;

namespace VanillaGraphicsExpanded.Rendering;

/// <summary>Distinguishes generated image allocation from content-only regeneration.</summary>
internal sealed partial class GpuResourceManager
{
    #region Internal API
    /// <summary>Publishes generated layout changes while preserving revisions for unchanged image descriptors.</summary>
    internal bool RefreshGeneratedTextureMetadata(GpuResourceMetadataEntry<GpuTextureMetadata> entry, bool wasAvailable)
    {
        RequireMetadataContext();
        RequireLiveEntry(entry);
        if (entry.OperationInProgress) throw new System.InvalidOperationException("An operation owns this resource result.");
        if (wasAvailable)
        {
            // Generation changes only descendants of the selected base image. Parameters,
            // the base image, and levels beyond the generated range remain established.
            int extent = 0;
            foreach (var image in entry.Metadata.Levels)
                if (image.Level == entry.Metadata.BaseLevel && image.Allocated)
                    extent = System.Math.Max(extent, System.Math.Max(image.Width,
                        entry.Metadata.Target == RHI.TextureDimension.OneArray ? 1 :
                        entry.Metadata.Target == RHI.TextureDimension.Three ? System.Math.Max(image.Height, image.Depth) : image.Height));
            int last = entry.Metadata.BaseLevel;
            while ((extent >>= 1) > 0) last++;
            last = System.Math.Min(last, entry.Metadata.MaxLevel);
            return RefreshTextureImages(entry, entry.Metadata.BaseLevel + 1, last, 0, true, true);
        }
        BeginMetadataCompletion(entry, GpuMetadataChange.None);
        entry.BeginRefresh(GpuMetadataChange.None);
        bool finished = false;
        bool notify = true;
        try
        {
            if (entry.Reader is not { } reader || !reader.TryRead(entry.Id, out var candidate)) return false;
            RequireMetadataContext();
            RequireLiveEntry(entry);
            // Compare native image facts rather than immutable-array instance identity.
            // Regeneration of existing levels changes pixels but does not replace storage.
            bool allocationChanged = !entry.Metadata.Levels.AsSpan().SequenceEqual(candidate.Levels.AsSpan());
            RequireUninterruptedCompletion(entry);
            entry.Publish(candidate, allocationChanged ? GpuMetadataChange.Storage : GpuMetadataChange.None);
            notify = allocationChanged || !wasAvailable;
            finished = true;
            return true;
        }
        finally
        {
            entry.CompletionInProgress = false;
            if (notify) entry.NotifyImageDependents(suppressErrors: !finished);
        }
    }
    #endregion
}
