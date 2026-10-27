namespace VanillaGraphicsExpanded.Rendering.RHI;

/// <summary>Declares native conditional-render waiting and region eligibility without reading query contents.</summary>
public enum ConditionalRenderPolicy
{
    Wait, NoWait, RegionWait, RegionNoWait, WaitInverted, NoWaitInverted, RegionWaitInverted, RegionNoWaitInverted
}
