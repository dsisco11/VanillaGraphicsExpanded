using VanillaGraphicsExpanded.Rendering.RHI;
using System;

namespace VanillaGraphicsExpanded.Rendering;

/// <summary>Provides available canonical executable snapshots without native queries or ownership reassociation.</summary>
internal sealed partial class RHIResourceManager
{
    #region Internal API
    /// <summary>Requires the source owner's available executable snapshot for executable consumers.</summary>
    internal static GpuProgramMetadata RequireProgramSnapshot(GpuProgramObject source)
    {
        ObjectDisposedException.ThrowIf(!source.IsValid, source);
        RequireWrapperMetadataAccess(source);
        RHICommandContext.RequireResourceOperationAvailable(GpuResourceKind.Program, source.ProgramId);
        if (source.ResourceManager?.GetResourceRegistration(source) is { } entry
            && !entry.Manager.IsResourceMetadataAvailable(source))
            throw new InvalidOperationException("The original program metadata is unavailable.");
        if (!source.HasMetadataSnapshot) throw new InvalidOperationException("The local executable snapshot is unavailable.");
        var data = source.MetadataSnapshot;
        if (!data.ExecutableAvailable) throw new InvalidOperationException("A linked executable is required for preparation.");
        return data;
    }

    #endregion
}
