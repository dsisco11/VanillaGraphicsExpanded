using System;
using System.Runtime.CompilerServices;

namespace VanillaGraphicsExpanded.Rendering;

/// <summary>Keeps wrapper association identity and metadata usability entirely inside the manager.</summary>
internal sealed partial class GpuResourceManager
{
    // Weak keys preserve the original registration while a wrapper lives without retaining wrappers.
    // Unregistration removes delivery membership, but must not let an old wrapper adopt a reused ID.
    private readonly ConditionalWeakTable<GpuResource, GpuResourceRegistration> resourceAssociations = new();

    #region Internal API
    /// <summary>Records the original registration without storing state or a back-reference in the wrapper.</summary>
    internal void RecordResourceAssociation(GpuResourceRegistration entry, GpuResource resource)
    {
        bool admitted = false;
        var original = resourceAssociations.GetValue(resource, _ => { admitted = true; return entry; });
        if (admitted)
        {
            var references = resource.UsageReferences;
            for (int index = 0; index < references.Pass; index++) entry.Retain(true);
            for (int index = 0; index < references.Lifetime; index++) entry.Retain(false);
        }
        if (!ReferenceEquals(original, entry))
            throw new InvalidOperationException("The wrapper belongs to a different original registration.");
    }

    /// <summary>Returns manager-owned association identity, including a retired original registration.</summary>
    internal GpuResourceRegistration? GetResourceRegistration(GpuResource resource)
    {
        return resourceAssociations.TryGetValue(resource, out var entry) ? entry : null;
    }

    /// <summary>Reports a live subscription independently of whether its metadata is currently available.</summary>
    internal bool IsResourceMetadataTracked(GpuResource resource)
    {
        return !resource.IsDisposed && GetResourceRegistration(resource) is { } entry
            && IsRegistered(entry) && entry.State == GpuResourceRegistrationState.Live && entry.ContainsRecipient(resource);
    }

    /// <summary>Reports canonical usability and successful snapshot delivery for this recipient.</summary>
    internal bool IsResourceMetadataAvailable(GpuResource resource)
    {
        return !resource.IsDisposed && GetResourceRegistration(resource) is { } entry
            && IsRegistered(entry) && entry.IsUsable && entry.HasSnapshot(resource)
            && IsBufferTextureBackingAvailable(entry) && IsRenderbufferTextureBackingAvailable(entry)
            && IsNativeTextureViewSourceAvailable(entry);
    }

    /// <summary>Reports retirement of a wrapper's original native registration without consulting a reused ID.</summary>
    internal bool IsResourceMetadataRetired(GpuResource resource)
    {
        return GetResourceRegistration(resource) is { } entry
            && (IsMetadataRegistryRetired || entry.State != GpuResourceRegistrationState.Live);
    }
    #endregion
}
