using System;
using System.Threading;

namespace VanillaGraphicsExpanded.Rendering;

/// <summary>Stores shared identity, availability and revisions for one manager registry entry.</summary>
internal abstract class GpuResourceRegistration
{
    private int state;
    private int metadataAvailable;

    #region Internal API
    #region Registration lifecycle
    internal RHIResourceManager Manager { get; }
    internal GpuResourceKind Kind { get; }
    internal nint Id { get; }
    internal GpuResourceHandle Handle => new(Kind, Id);
    internal GpuResourceRegistrationState State => (GpuResourceRegistrationState)Volatile.Read(ref state);
    internal bool MetadataAvailable
    {
        get => !Manager.IsMetadataRegistryRetired && State == GpuResourceRegistrationState.Live
            && Volatile.Read(ref metadataAvailable) != 0;
        set => Volatile.Write(ref metadataAvailable, value ? 1 : 0);
    }
    internal ulong MetadataRevision { get; set; }
    internal ulong StorageRevision { get; set; }
    /// <summary>Distinguishes a generated name from an object with established native defaults.</summary>
    internal bool IsRealized { get; private set; }
    /// <summary>Authorizes one explicit initial discovery for its recorded lifecycle cause.</summary>
    internal GpuInitialDiscovery InitialDiscovery { get; set; }
    internal bool IsUsable => !Manager.IsMetadataRegistryRetired && State == GpuResourceRegistrationState.Live && MetadataAvailable;
    internal abstract int AssociationCount { get; }

    /// <summary>Captures the registry owner and native identity without granting native ownership.</summary>
    internal GpuResourceRegistration(RHIResourceManager manager, GpuResourceKind kind, nint id)
    {
        Manager = manager;
        Kind = kind;
        Id = id;
    }

    /// <summary>Realizes the original registration without renewing its allocation identity.</summary>
    internal void Realize() => IsRealized = true;

    /// <summary>Marks the canonical entry retiring before owning retirement is queued.</summary>
    internal bool TryRetire()
    {
        if (Interlocked.CompareExchange(ref state, (int)GpuResourceRegistrationState.Retiring,
            (int)GpuResourceRegistrationState.Live) != (int)GpuResourceRegistrationState.Live) return false;
        MetadataAvailable = false;

        return true;
    }

    /// <summary>Invalidates the registration permanently when its name or context is retired.</summary>
    internal void Invalidate(GpuResourceRegistrationState terminalState)
    {
        Interlocked.Exchange(ref state, (int)terminalState);
        MetadataAvailable = false;
        ClearAssociations();
    }

    #endregion


    #region Wrapper associations
    /// <summary>Removes dead wrapper associations without discarding canonical metadata.</summary>
    internal abstract void PruneAssociations();

    /// <summary>Removes one wrapper association without changing other aliases or native ownership.</summary>
    internal abstract void Unregister(GpuResource resource);

    /// <summary>Registers a selected image in the same weak recipient collection as ordinary typed wrappers.</summary>
    internal abstract void AssociateImage(GpuResource<GpuImageMetadata> resource);

    /// <summary>Checks whether the wrapper remains in this entry's recipient collection.</summary>
    internal abstract bool ContainsRecipient(GpuResource resource);

    /// <summary>Checks whether this recipient received a complete snapshot in the latest publication.</summary>
    internal abstract bool HasSnapshot(GpuResource resource);

    /// <summary>Releases weak association storage on terminal invalidation.</summary>
    internal abstract void ClearAssociations();

    /// <summary>Captures exactly-once terminal notification before recipient membership is released.</summary>
    internal abstract Action CaptureRetirement();
    #endregion
    #endregion
}

/// <summary>Describes registration lifecycle independently of native metadata and numeric name reuse.</summary>
internal enum GpuResourceRegistrationState { Live, Retiring, Deleted, ContextRetired }

/// <summary>Classifies a proven metadata effect without treating content writes as metadata changes.</summary>
[Flags]
internal enum GpuMetadataChange { None = 0, Metadata = 1, Storage = 2, Executable = 4 }
