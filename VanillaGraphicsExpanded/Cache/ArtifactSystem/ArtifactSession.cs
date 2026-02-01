using System.Threading;

namespace VanillaGraphicsExpanded.Cache.ArtifactSystem;

internal enum ArtifactExecutionMode : byte
{
    Background = 0,
    InlineCurrentThread = 1,
}

/// <summary>
/// Identifies a scheduler run. Any outputs produced under an older SessionId are stale and must be dropped.
/// </summary>
internal readonly record struct ArtifactSession(long SessionId, CancellationToken CancellationToken, ArtifactExecutionMode Mode);
