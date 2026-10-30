using System;

namespace VanillaGraphicsExpanded.Rendering;

/// <summary>Certifies texture consumers using existing associations and published snapshots.</summary>
internal sealed partial class RHIResourceManager
{
    #region Internal API
    /// <summary>Distinguishes absent or retired input from a live resource whose facts are unavailable.</summary>
    internal static bool ValidateTextureReferenceInput(GpuResource<GpuTextureMetadata>? resource)
    {
        if (!IsTextureReferenceLifetimeLive(resource)) return false;
        RequireTextureReferenceMetadata(resource);
        return true;
    }
    /// <summary>Checks texture usability without native discovery or wrapper validity recursion.</summary>
    internal static bool TryGetTextureReferenceMetadata(GpuResource<GpuTextureMetadata>? resource,
        out GpuTextureMetadata data)
    {
        data = default;
        if (resource is null || !resource.HasMetadataSnapshot || !IsTextureReferenceLifetimeLive(resource)) return false;
        var manager = resource.ResourceManager;
        if (manager?.GetResourceRegistration(resource) is not null && !manager.IsResourceMetadataAvailable(resource))
            return false;
        var snapshot = resource.PublishedMetadata;
        if (snapshot.Levels.IsDefaultOrEmpty && snapshot.Buffer is null) return false;
        if (snapshot.Buffer is { BufferId: 0 }) return false;
        if (resource is GpuBufferView bufferView && !IsBufferTextureSourceCurrent(bufferView, snapshot)) return false;
        // Check original source storage before compatibility history can be reused.
        if (resource is GpuTextureView view && view.Source is { } source)
        {
            if (!TryGetTextureReferenceMetadata(source, out _) || (int)source.ResourceId != view.SourceTextureId
                || TextureStorageRevision(source) != view.SourceGeneration
                || !(manager?.IsTextureViewSourceCurrent(view, source)
                    ?? ReferenceEquals(TextureStorageIdentity(source), view.SourceStorageIdentity))) return false;
        }
        data = snapshot;
        return true;
    }

    /// <summary>Requires usable facts and originating context before submitting a texture reference.</summary>
    internal static GpuTextureMetadata RequireTextureReferenceMetadata(GpuResource<GpuTextureMetadata>? resource)
    {
        if (!TryGetTextureReferenceMetadata(resource, out var data))
            throw new InvalidOperationException("Texture reference metadata is unavailable or retired.");
        resource!.RequireOriginalContext();
        RequireWrapperMetadataAccess(resource);
        return data;
    }
    #endregion

    #region Private
    /// <summary>Separates retired dependency identity from recoverable snapshot unavailability.</summary>
    private static bool IsTextureReferenceLifetimeLive(GpuResource<GpuTextureMetadata>? resource)
    {
        if (resource is null || !((GpuResource)resource).IsValid) return false;
        if (resource is not GpuTextureView view || view.Source is not { } source) return true;
        if (!((GpuResource)source).IsValid || (int)source.ResourceId != view.SourceTextureId
            || TextureStorageRevision(source) != view.SourceGeneration) return false;
        // The association comparison intentionally ignores availability: callers must reject
        // missing facts, whereas an optional input with retired storage can clear its slot.
        if (resource.ResourceManager is { } manager && manager.GetResourceRegistration(view) is { } entry
            && manager.textureBackingIdentities.TryGetValue(entry, out var dependency))
            return ReferenceEquals(dependency.Source, manager.GetResourceRegistration(source));
        return ReferenceEquals(TextureStorageIdentity(source), view.SourceStorageIdentity);
    }
    #endregion
}
