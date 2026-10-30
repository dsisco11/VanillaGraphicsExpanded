using System;
using System.Collections.Generic;
using Graffs;
using Graffs.Implementations;

namespace VanillaGraphicsExpanded.Rendering;

/// <summary>Indexes original resource relationships without owning their metadata or native lifetimes.</summary>
internal sealed partial class RHIResourceManager
{
    private readonly object dependencySync = new();
    private readonly MutableDirectedGraph<GpuResourceRegistration> resourceDependencies = new(ReferenceEqualityComparer.Instance);

    #region Private
    /// <summary>Returns a stable neighborhood, reusing Graffs' snapshot until its edges change.</summary>
    private IReadOnlySet<GpuResourceRegistration> GetResourceDependents(GpuResourceRegistration source)
    {
        lock (dependencySync)
            return ((IDirectedGraph<GpuResourceRegistration>)resourceDependencies).GetDependents(source);
    }

    /// <summary>Records an edge only while its original source still has a pending native lifetime.</summary>
    private void AddResourceDependency(GpuResourceRegistration source, GpuResourceRegistration dependent)
    {
        lock (dependencySync)
        {
            if (IsMetadataRegistryRetired || !IsRegistered(source) || !IsRegistered(dependent)
                || dependent.State != GpuResourceRegistrationState.Live) return;
            resourceDependencies.AddEdge(source, dependent);
        }
    }

    /// <summary>Removes one association and releases isolated lookup nodes without touching registrations.</summary>
    private void RemoveResourceDependency(GpuResourceRegistration source, GpuResourceRegistration dependent)
    {
        lock (dependencySync)
        {
            resourceDependencies.RemoveEdge(source, dependent);
            PruneDependencyNode(source);
            PruneDependencyNode(dependent);
        }
    }

    /// <summary>Drops an isolated graph node; callers hold the dependency lock.</summary>
    private void PruneDependencyNode(GpuResourceRegistration entry)
    {
        if (resourceDependencies.GetDependencies(entry).Count == 0 && resourceDependencies.GetDependents(entry).Count == 0)
            resourceDependencies.RemoveNode(entry);
    }

    /// <summary>Retires the dependent side while preserving outgoing edges needed for deferred deletion.</summary>
    private void RetireDependentRelationships(GpuResourceRegistration entry)
    {
        lock (dependencySync)
        {
            foreach (var source in resourceDependencies.GetDependenciesSnapshot(entry))
            {
                resourceDependencies.RemoveEdge(source, entry);
                PruneDependencyNode(source);
            }
            PruneDependencyNode(entry);
            textureBackingIdentities.Remove(entry);
            unresolvedDependencies.Remove(entry);
        }
    }

    /// <summary>Releases final graph retention after native deletion has processed affected descriptors.</summary>
    private void RemoveDeletedRelationships(GpuResourceRegistration entry)
    {
        lock (dependencySync)
        {
            var sources = resourceDependencies.GetDependenciesSnapshot(entry);
            var dependents = resourceDependencies.GetDependentsSnapshot(entry);
            resourceDependencies.RemoveNode(entry);
            foreach (var source in sources) PruneDependencyNode(source);
            foreach (var dependent in dependents) PruneDependencyNode(dependent);
            textureBackingIdentities.Remove(entry);
            unresolvedDependencies.Remove(entry);
        }
    }
    #endregion
}
