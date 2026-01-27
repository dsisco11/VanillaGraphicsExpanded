using System.Threading.Tasks;

namespace VanillaGraphicsExpanded.Cache.ArtifactSystem;

/// <summary>
/// Single post-compute output stage.
/// Encapsulates any disk and GPU work needed to "materialize" the artifact.
/// </summary>
internal interface IArtifactOutputStage<TKey, TOutput>
{
    ValueTask OutputAsync(ArtifactOutputContext<TKey, TOutput> context);
}
