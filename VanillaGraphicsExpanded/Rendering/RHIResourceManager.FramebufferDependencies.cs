using System.Collections.Generic;
using System.Runtime.CompilerServices;

namespace VanillaGraphicsExpanded.Rendering;

/// <summary>Derives attachment lookup membership from committed original backing descriptors.</summary>
internal sealed partial class RHIResourceManager
{
    private readonly ConditionalWeakTable<GpuResourceRegistration, object> unresolvedDependencies = new();
    private static readonly object unresolvedDependency = new();

    #region Internal API
    /// <summary>Updates attachment edges at canonical commit before projections or notifications can run.</summary>
    internal void ReconcilePublishedDependencies<T>(GpuResourceRegistration entry, T previous, T candidate) where T : struct
    {
        if (candidate is GpuTextureMetadata)
        {
            // A complete texture candidate has either a captured backing or no association.
            lock (dependencySync) unresolvedDependencies.Remove(entry);
            return;
        }
        if (candidate is not GpuFramebufferMetadata framebuffer) return;
        if (previous is GpuFramebufferMetadata old && old.Equals(framebuffer)) return;
        lock (dependencySync)
        {
            if (IsMetadataRegistryRetired || entry.State != GpuResourceRegistrationState.Live) return;
            var sources = new HashSet<GpuResourceRegistration>(System.Collections.Generic.ReferenceEqualityComparer.Instance);
            bool unresolved = false;
            if (framebuffer.Attachments is { } attachments)
                foreach (var attachment in attachments.Values)
                    if (attachment.OriginalBacking is { } source) sources.Add(source);
                    else if (attachment.Id != 0) unresolved = true;

            // Compare unique sources, not slots: another attachment may still retain the same source.
            foreach (var source in resourceDependencies.GetDependenciesSnapshot(entry))
                if (!sources.Contains(source)) RemoveResourceDependency(source, entry);
            foreach (var source in sources) AddResourceDependency(source, entry);
            unresolvedDependencies.Remove(entry);
            if (unresolved) unresolvedDependencies.Add(entry, unresolvedDependency);
        }
    }
    #endregion

    #region Private
    /// <summary>Snapshots only exceptional unresolved relationships, never all registered resources.</summary>
    private IReadOnlyCollection<GpuResourceRegistration> GetDeletionDependents(GpuResourceRegistration? source)
    {
        lock (dependencySync)
        {
            var affected = new HashSet<GpuResourceRegistration>(System.Collections.Generic.ReferenceEqualityComparer.Instance);
            if (source is not null) affected.UnionWith(GetResourceDependents(source));
            foreach (var pair in unresolvedDependencies) affected.Add(pair.Key);
            return affected;
        }
    }
    #endregion
}
