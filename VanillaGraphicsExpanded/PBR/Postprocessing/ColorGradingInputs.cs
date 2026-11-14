using System;
using VanillaGraphicsExpanded.Rendering;
namespace VanillaGraphicsExpanded.PBR.Postprocessing;
/// <summary>Packs named display transform controls for lookup generation.</summary>
internal sealed class ColorGradingInputs : CpuUniformBuffer
{
    #region Public API
    internal const int BlockSize = 16;
    internal const int ControlsOffset = 0;
    /// <summary>Declares this layout's immutable extent.</summary>
    public override int SizeBytes => BlockSize;
    /// <summary>Checks the complete cache key before admission or generation changes.</summary>
    internal static void Validate(ColorGradingParameters controls)
    {
        if (!float.IsFinite(controls.Gamma) || !float.IsFinite(controls.ExtraGamma) ||
            !float.IsFinite(controls.Brightness) || !float.IsFinite(controls.Contrast))
            throw new ArgumentException("Display grading requires finite controls.", nameof(controls));
    }
    /// <summary>Packs the admitted transform without exposing its component layout to callers.</summary>
    internal void Capture(ColorGradingParameters controls)
    {
        Validate(controls);
        WriteVector4(ControlsOffset, new(controls.Gamma, controls.ExtraGamma, controls.Brightness, controls.Contrast));
    }
    #endregion
}
