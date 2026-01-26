using System.Threading.Tasks;

namespace VanillaGraphicsExpanded.Cache.Artifacts;

/// <summary>
/// Worker-thread compute stage.
/// Threading contract: no GL calls; CPU work and asset decode are permitted.
/// </summary>
internal interface IArtifactComputer<TKey, TOutput>
{
    ValueTask<ArtifactComputeResult<TOutput>> ComputeAsync(ArtifactComputeContext<TKey> context);
}
