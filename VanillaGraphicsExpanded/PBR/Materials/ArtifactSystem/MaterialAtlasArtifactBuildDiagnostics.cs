using VanillaGraphicsExpanded.Cache.ArtifactSystem;

namespace VanillaGraphicsExpanded.PBR.Materials.ArtifactSystem;

internal readonly record struct MaterialAtlasArtifactBuildDiagnostics(
    int GenerationId,
    int Remaining,
    bool IsComplete,
    ArtifactSchedulerStats MaterialParams,
    ArtifactSchedulerStats NormalDepth);
