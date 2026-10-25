using System;


namespace VanillaGraphicsExpanded.Rendering;

/// <summary>Supplies canonical executable facts to preparation without a second independent native inspection.</summary>
internal sealed partial class GpuResourceManager
{
    #region Internal API
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
