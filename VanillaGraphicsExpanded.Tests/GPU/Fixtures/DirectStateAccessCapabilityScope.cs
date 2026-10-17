using HarmonyLib;
using VanillaGraphicsExpanded.Rendering;

namespace VanillaGraphicsExpanded.Tests.GPU.Fixtures;

/// <summary>Temporarily disables an advertised optional capability without adding runtime testing switches.</summary>
internal sealed class DirectStateAccessCapabilityScope:IDisposable
{
    private readonly GraphicsCapabilities original;
    #region Public API
    /// <summary>Publishes a copied capability record while retaining the exact current context registration.</summary>
    internal DirectStateAccessCapabilityScope(bool enabled)
    {
        original=GpuSupport.Graphics;
        if(enabled&&!original.SupportsArbDirectStateAccess)throw new InvalidOperationException("Current context does not support named framebuffer state.");
        AccessTools.Field(typeof(GpuSupport),"graphics").SetValue(null,original with{SupportsArbDirectStateAccess=enabled});
    }
    /// <summary>Restores the original immutable capability snapshot after success or failure.</summary>
    public void Dispose()=>AccessTools.Field(typeof(GpuSupport),"graphics").SetValue(null,original);
    #endregion
}
