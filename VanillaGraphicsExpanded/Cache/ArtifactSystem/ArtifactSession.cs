using System.Threading;

namespace VanillaGraphicsExpanded.Cache.Artifacts;

/// <summary>
/// Identifies a scheduler run. Any outputs produced under an older SessionId are stale and must be dropped.
/// </summary>
internal readonly record struct ArtifactSession(long SessionId, CancellationToken CancellationToken);
