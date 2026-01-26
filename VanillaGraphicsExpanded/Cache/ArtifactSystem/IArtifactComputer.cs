using System.Threading.Tasks;

namespace VanillaGraphicsExpanded.Cache.Artifacts;

/// <summary>
/// Worker-thread compute stage.
/// Threading contract: no GL calls; CPU work and asset decode are permitted.
/// </summary>
internal interface IArtifactComputer<TKey, TDiskPayload, TGpuPayload>
{
    ValueTask<ArtifactComputeResult<TDiskPayload, TGpuPayload>> ComputeAsync(ArtifactComputeContext<TKey> context);
}
