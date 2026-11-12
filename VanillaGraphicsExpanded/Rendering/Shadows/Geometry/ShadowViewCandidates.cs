using VanillaGraphicsExpanded.Rendering.Shadows.Candidates;

namespace VanillaGraphicsExpanded.Rendering.Shadows.Geometry;

/// <summary>Associates a current receiver enclosure with one virtual view without retaining native geometry.</summary>
internal readonly record struct ShadowViewCandidates(int View, ShadowCandidateHandle Handle,
    ShadowCandidateCurrent Current);
