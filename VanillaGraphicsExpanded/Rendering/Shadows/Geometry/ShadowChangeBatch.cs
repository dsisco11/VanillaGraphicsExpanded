using System.Collections.Immutable;

namespace VanillaGraphicsExpanded.Rendering.Shadows.Geometry;

/// <summary>Requires broad invalidation on lost history and prohibits certification during pending changes.</summary>
internal sealed record ShadowChangeBatch(long Sequence, long Epoch, bool Complete, bool RequiresBroadInvalidation,
    ImmutableArray<ShadowSceneChange> Changes);
