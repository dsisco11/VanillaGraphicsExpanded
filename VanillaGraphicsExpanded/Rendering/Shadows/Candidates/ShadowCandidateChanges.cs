using System.Collections.Immutable;
using VanillaGraphicsExpanded.Rendering.Shadows.World;

namespace VanillaGraphicsExpanded.Rendering.Shadows.Candidates;

/// <summary>Freezes a contiguous changed-partition interval; unavailable history cannot certify reuse.</summary>
internal sealed record ShadowCandidateChanges(long Epoch, long FromSequence, long ToSequence,
    bool Complete, ImmutableArray<ShadowChunkIdentity> Coordinates);
