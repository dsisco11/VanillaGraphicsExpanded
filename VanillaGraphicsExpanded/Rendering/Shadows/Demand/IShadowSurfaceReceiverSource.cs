using VanillaGraphicsExpanded.Rendering.Shadows.Lights;
using VanillaGraphicsExpanded.Rendering.Shadows.Scheduling;

namespace VanillaGraphicsExpanded.Rendering.Shadows.Demand;

/// <summary>Surface owners supply all late/transparent layers early, using bounded conservative bounds or an equivalent current prepass.</summary>
internal interface IShadowSurfaceReceiverSource
{
    /// <summary>Charges preparation before effects and returns current deformation-inclusive receiving bounds.</summary>
    ShadowSurfacePublication Capture(ShadowLightSnapshot lights,ShadowFrameBudget budget);
}
