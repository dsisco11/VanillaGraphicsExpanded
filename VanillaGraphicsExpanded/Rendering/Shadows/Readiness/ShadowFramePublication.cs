using VanillaGraphicsExpanded.Rendering.Shadows.Lights;
using VanillaGraphicsExpanded.Rendering.Shadows.Pages;

namespace VanillaGraphicsExpanded.Rendering.Shadows.Readiness;

/// <summary>One current-light interval's GPU eligibility and selected rendered mappings, borrowed until surface submission ends.</summary>
internal sealed class ShadowFramePublication
{
    private bool active=true;
    internal long Frame { get; }
    internal ShadowLightSnapshot Lights { get; }
    internal ShadowPagePool Pages { get; }
    internal GpuShaderStorageBuffer Requests { get; }
    internal GpuShaderStorageBuffer Readiness { get; }
    /// <summary>Rejects ended, superseded, disabled, reconfigured and capability-withdrawn intervals before binding.</summary>
    internal bool IsCurrent=>active&&Pages.CanSubmit;

    #region Public API
    /// <summary>Borrows the exact owning frame resources without taking their allocation or retirement authority.</summary>
    internal ShadowFramePublication(ShadowLightSnapshot lights,ShadowPagePool pages,
        GpuShaderStorageBuffer requests,GpuShaderStorageBuffer readiness)
    {Frame=lights.Frame;Lights=lights;Pages=pages;Requests=requests;Readiness=readiness;}
    /// <summary>Withdraws this interval permanently so reopening the same pool cannot revive it.</summary>
    internal void Withdraw(){active=false;}
    #endregion
}
