using System;

namespace VanillaGraphicsExpanded.Rendering;

/// <summary>Validates fixed executable associations without acquiring or reflecting replacement metadata.</summary>
internal sealed partial class RHIResourceManager
{
    #region Internal API
    /// <summary>Requires the originally captured program association and published executable revision.</summary>
    internal static void RequireFixedExecutable(GpuProgramObject program, GpuResourceRegistration? original,
        ulong revision, GpuProgramMetadata snapshot)
    {
        program.RequireOriginalContext();
        var current = RequireProgramSnapshot(program);
        var entry = program.ResourceManager?.GetResourceRegistration(program);
        if (!ReferenceEquals(entry, original) || (entry is not null ? entry.MetadataRevision != revision : !current.Equals(snapshot)))
            throw new InvalidOperationException("The shader executable or its binding interface changed after realization.");
    }
    #endregion
}
