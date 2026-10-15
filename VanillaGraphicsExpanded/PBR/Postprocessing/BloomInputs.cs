using System.Numerics;
using VanillaGraphicsExpanded.PBR.Postprocessing.LocalExposure;
using VanillaGraphicsExpanded.Rendering;

namespace VanillaGraphicsExpanded.PBR.Postprocessing;

/// <summary>Publishes bloom filtering and source-local exposure policy in six std140 vector slots.</summary>
internal sealed class BloomInputs : CpuUniformBuffer
{
    #region Public API
    /// <summary>Allocates filtering parameters followed by optional source exposure controls.</summary>
    internal BloomInputs() : base(96) { }

    /// <summary>Captures one filter draw without changing the coherent source exposure policy.</summary>
    internal void Capture(Vector4 pass, Vector4 effect, Vector4 sun = default, Vector4 solar = default)
    {
        WriteVector4(0, pass); WriteVector4(16, effect);
        WriteVector4(32, sun); WriteVector4(48, solar);
    }

    /// <summary>Captures the same bounded correction and middle-gray pivot used by the scene display.</summary>
    internal void CaptureLocal(LocalExposureParameters settings, float logMiddleGray)
    {
        WriteVector4(64, new(settings.ShadowStrength, settings.HighlightStrength, settings.ShadowLimit, settings.HighlightLimit));
        WriteVector4(80, new(settings.RangeWidth, settings.Enabled ? 1 : 0, logMiddleGray, 0));
    }
    #endregion
}
