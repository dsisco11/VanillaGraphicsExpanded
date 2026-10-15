namespace VanillaGraphicsExpanded.PBR.CameraExposure;

/// <summary>Validated frame snapshot used by metering, adaptation and manual display binding.</summary>
internal readonly record struct CameraExposureParameters(bool Enabled, float Compensation, float ManualLogGain,
    float MinLogGain, float MaxLogGain, float MiddleGray, float LowPercentile, float HighPercentile,
    float BrightenRate, float DarkenRate, bool CenterWeighted)
{
    #region Public API
    /// <summary>Centers automatic display contrast with compensation and manual contrast at fixed middle gray.</summary>
    internal float DisplayLogMiddleGray => System.MathF.Log2(MiddleGray)+(Enabled ? Compensation : 0);
    #endregion
}
