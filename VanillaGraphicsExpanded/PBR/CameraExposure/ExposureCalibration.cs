using System;

namespace VanillaGraphicsExpanded.PBR.CameraExposure;

/// <summary>Defines the virtual camera's saturation-based EV100 calibration independently of display grading.</summary>
internal static class ExposureCalibration
{
    #region Public API
    /// <summary>Lens throughput chosen for the calibrated virtual camera.</summary>
    internal const float LensAttenuation = .78f;
    /// <summary>Saturation luminance at camera EV100 zero, in cd/m².</summary>
    internal const float SaturationLuminance = .78f / LensAttenuation;
    /// <summary>Converts authored camera EV100 to uncompensated display log2 gain.</summary>
    internal static float ToLogGain(float cameraEV100) => -cameraEV100 - MathF.Log2(SaturationLuminance);
    /// <summary>Converts applied log2 gain to its equivalent uncompensated camera EV100.</summary>
    internal static float FromLogGain(float logGain) => -logGain - MathF.Log2(SaturationLuminance);
    /// <summary>Computes the camera EV100 that maps measured luminance to the authored middle gray.</summary>
    internal static float SceneEV100(float logLuminance, float middleGray)
        => logLuminance - MathF.Log2(middleGray * SaturationLuminance);
    #endregion
}
