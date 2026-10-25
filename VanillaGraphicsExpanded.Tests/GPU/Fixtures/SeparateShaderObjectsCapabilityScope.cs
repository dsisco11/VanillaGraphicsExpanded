using HarmonyLib;
using VanillaGraphicsExpanded.Rendering;

namespace VanillaGraphicsExpanded.Tests.GPU.Fixtures;

/// <summary>Exercises bound uniform dispatch by temporarily withdrawing separate shader object support.</summary>
internal sealed class SeparateShaderObjectsCapabilityScope : IDisposable
{
    private readonly GraphicsCapabilities original;

    #region Public API
    /// <summary>Overrides the cached dispatch capability without changing the native context.</summary>
    internal SeparateShaderObjectsCapabilityScope(bool enabled)
    {
        original = GpuSupport.Graphics;
        if (enabled && !original.SupportsArbSeparateShaderObjects)
            throw new InvalidOperationException("Current context does not support separate shader objects.");
        AccessTools.Field(typeof(GpuSupport), "graphics").SetValue(null,
            original with { SupportsArbSeparateShaderObjects = enabled });
    }

    /// <summary>Restores the capability snapshot after the test.</summary>
    public void Dispose() => AccessTools.Field(typeof(GpuSupport), "graphics").SetValue(null, original);
    #endregion
}
