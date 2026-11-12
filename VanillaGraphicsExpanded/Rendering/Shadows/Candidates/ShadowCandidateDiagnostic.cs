using VanillaGraphicsExpanded.Numerics;
using VanillaGraphicsExpanded.Rendering.Shadows.Geometry;
using VanillaGraphicsExpanded.Rendering.Shadows.Lights;

namespace VanillaGraphicsExpanded.Rendering.Shadows.Candidates;

/// <summary>CPU-known association age and conservative covered motion, separate from GPU traversal completeness.</summary>
internal sealed record ShadowCandidateDiagnostic(ShadowSourceKey Source,int Slot,long AgeFrames,bool IsPoint,
    ShadowBounds CoveredSourceBox,Vector3d AnchorDirection,double CoveredDegrees,double NormalizedMotion,bool NeedsRefresh);
