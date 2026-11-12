using VanillaGraphicsExpanded.Rendering.Shadows.Lights;
using VanillaGraphicsExpanded.Rendering.Shadows.Scheduling;

namespace VanillaGraphicsExpanded.Rendering.Integration.Shadows;

/// <summary>Consumer-owned early scene/demand boundary; implementations reserve work before producing current bounded inputs.</summary>
internal interface IShadowFrameSource:System.IDisposable
{
    /// <summary>All current world/candidate/preparation owners and their in-flight retirement, excluding the gate's own resources.</summary>
    long LiveAndRetiringBytes { get; }
    /// <summary>Captures every contributing surface family using this exact current light snapshot and one shared ledger.</summary>
    NativeShadowFrameInput Capture(ShadowLightSnapshot lights,float deltaTime,ShadowFrameBudget budget);
}
