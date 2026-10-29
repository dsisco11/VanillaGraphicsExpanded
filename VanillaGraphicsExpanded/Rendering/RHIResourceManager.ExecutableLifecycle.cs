using System.Collections.Immutable;
using VanillaGraphicsExpanded.Rendering.RHI;

namespace VanillaGraphicsExpanded.Rendering;

/// <summary>Maintains original shader, program and pipeline identities from their native factories.</summary>
internal sealed partial class RHIResourceManager
{
    #region Internal API
    /// <summary>Records owner-supplied executable provenance before interception, without inspecting native state.</summary>
    internal GpuResourceMetadataEntry<T> RecordPreexistingExecutable<T>(GpuResourceKind kind, nint id) where T : struct
    {
        RequireMetadataContext();
        if (resourceObservationActive || kind is not (GpuResourceKind.Shader or GpuResourceKind.Program or GpuResourceKind.ProgramPipeline))
            throw new System.InvalidOperationException("Executable provenance must precede observation activation.");
        if (FindResource(kind, id) is not null)
            throw new System.InvalidOperationException("The original executable identity is already registered.");
        var entry = RegisterCreatedResource<T>(kind, id);
        entry.Realize();
        entry.InitialDiscovery = GpuInitialDiscovery.Preexisting;
        return entry;
    }

    /// <summary>Publishes guaranteed factory facts once without overwriting an existing live allocation.</summary>
    internal void ObserveExecutableCreation(GpuResourceKind kind, nint id, ShaderStage stage = default, bool realized = true)
    {
        if (id == 0 || !CanObserveNativeMetadata || FindResource(kind, id) is not null) return;
        if (kind == GpuResourceKind.Shader)
            PublishMetadata(RegisterCreatedResource<GpuShaderMetadata>(kind, id), new(stage), GpuMetadataChange.None);
        else if (kind == GpuResourceKind.Program)
            PublishMetadata(RegisterCreatedResource<GpuProgramMetadata>(kind, id), new(false,
                ImmutableArray<GpuProgramResourceMetadata>.Empty, ImmutableArray<GpuProgramTextureUnitMetadata>.Empty), GpuMetadataChange.None);
        else if (kind == GpuResourceKind.ProgramPipeline)
        {
            var entry = RegisterCreatedResource<GpuProgramPipelineMetadata>(kind, id);
            if (realized) PublishMetadata(entry, new GpuProgramPipelineMetadata(), GpuMetadataChange.None);
        }
    }

    /// <summary>Realizes only an observed pipeline reservation; unknown names never acquire guessed defaults.</summary>
    internal bool RealizeObservedProgramPipeline(int id)
    {
        if (FindObservedResource(GpuResourceKind.ProgramPipeline, id) is not GpuResourceMetadataEntry<GpuProgramPipelineMetadata> entry)
            return false;
        if (!entry.IsRealized) PublishMetadata(entry, new GpuProgramPipelineMetadata(), GpuMetadataChange.None);
        return entry.IsUsable;
    }
    #endregion
}
