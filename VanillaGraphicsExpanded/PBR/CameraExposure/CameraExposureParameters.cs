namespace VanillaGraphicsExpanded.PBR.CameraExposure;

/// <summary>Validated frame snapshot used by metering, adaptation and manual display binding.</summary>
internal readonly record struct CameraExposureParameters(bool Enabled, float Compensation, float ManualEV,
    float MinEV, float MaxEV, float MiddleGray, float LowPercentile, float HighPercentile,
    float BrightenRate, float DarkenRate, bool CenterWeighted);
