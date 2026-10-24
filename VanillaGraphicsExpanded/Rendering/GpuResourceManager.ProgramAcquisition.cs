using System;
using System.Collections.Immutable;

namespace VanillaGraphicsExpanded.Rendering;

/// <summary>Supplies canonical executable facts to preparation without a second independent native inspection.</summary>
internal sealed partial class GpuResourceManager
{
    #region Internal API
    /// <summary>Publishes the specification-defined empty executable of a newly created program without a completion query.</summary>
    internal static void AcquireCreatedProgramMetadata(GpuProgramObject program)
    {
        // The factory has just received a nonzero glCreateProgram result. Unlike
        // adoption, this boundary proves an unlinked object with no active interface.
        PublishObservedMetadata(program, new GpuProgramMetadata(false,
            ImmutableArray<GpuProgramResourceMetadata>.Empty,
            ImmutableArray<GpuProgramTextureUnitMetadata>.Empty), GpuMetadataChange.None);
        if (program.ResourceManager?.GetResourceRegistration(program) is GpuResourceMetadataEntry<GpuProgramMetadata> entry)
            entry.Reader ??= GpuProgramMetadataReader.Instance;
    }

    /// <summary>Reuses tracked facts or acquires the program once at its explicit preparation boundary.</summary>
    internal static GpuProgramMetadata AcquireProgramMetadata(int program)
    {
        var manager = GpuResourceManagerSystem.CurrentManager;
        if (manager?.CanObserveNativeMetadata == true)
        {
            var entry = manager.AcquireResource(GpuResourceKind.Program, program, GpuProgramMetadataReader.Instance);
            if (entry is not { IsUsable: true } || !entry.Metadata.ExecutableAvailable)
                throw new InvalidOperationException("A linked program is required for preparation.");
            return entry.Metadata;
        }
        if (!GpuProgramMetadataReader.Instance.TryRead(program, out var metadata) || !metadata.ExecutableAvailable)
            throw new InvalidOperationException("Native executable facts are unavailable for preparation.");
        return metadata;
    }
    #endregion
}
