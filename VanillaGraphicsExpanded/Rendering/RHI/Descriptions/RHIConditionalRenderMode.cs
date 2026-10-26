namespace VanillaGraphicsExpanded.Rendering.RHI;

/// <summary>Declares native conditional-render waiting and region eligibility without reading query contents.</summary>
public enum RHIConditionalRenderMode
{
    Wait, NoWait, RegionWait, RegionNoWait, WaitInverted, NoWaitInverted, RegionWaitInverted, RegionNoWaitInverted
}
