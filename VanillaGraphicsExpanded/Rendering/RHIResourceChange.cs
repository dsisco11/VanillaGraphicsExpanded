using System;
namespace VanillaGraphicsExpanded.Rendering;

/// <summary>Describes a committed transition of one original resource lifetime.</summary>
internal readonly record struct RHIResourceChange(ResourceChangeKind Kind, bool Available,
    ulong MetadataRevision, ulong StorageRevision, GpuResource? Origin = null)
{
    /// <summary>Projects established effects for existing executable preparation.</summary>
    internal GpuMetadataChange Effects => ((Kind & ResourceChangeKind.Storage) != 0 ? GpuMetadataChange.Storage : 0)
        | ((Kind & ResourceChangeKind.Executable) != 0 ? GpuMetadataChange.Executable : 0)
        | ((Kind & ResourceChangeKind.Metadata) != 0 ? GpuMetadataChange.Metadata : 0);
}

/// <summary>Identifies meaningful facts, storage, executable and lifetime transitions.</summary>
[Flags]
internal enum ResourceChangeKind { None = 0, Metadata = 1, Storage = 2, Executable = 4, Availability = 8, Retired = 16 }
