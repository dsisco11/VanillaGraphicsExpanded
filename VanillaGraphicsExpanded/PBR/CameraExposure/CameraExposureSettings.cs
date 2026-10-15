using System;
namespace VanillaGraphicsExpanded.PBR.CameraExposure;

/// <summary>Persisted calibrated camera EV100 and physical-luminance metering policy.</summary>
[Newtonsoft.Json.JsonConverter(typeof(CameraExposureSettingsConverter))]
public sealed class CameraExposureSettings
{
    #region Public API
    /// <summary>Enables scene histogram metering and temporal adaptation.</summary>
    public bool Enabled { get; set; } = true;
    /// <summary>Biases the metered exposure in stops.</summary>
    public float Compensation { get; set; }
    /// <summary>Sets fixed camera EV100; increasing this value darkens the image.</summary>
    public float ManualEV100 { get; set; }
    /// <summary>Lowest camera EV100, limiting dark-scene brightening.</summary>
    public float MinEV100 { get; set; } = -2;
    /// <summary>Highest camera EV100, limiting bright-scene darkening.</summary>
    public float MaxEV100 { get; set; } = 20;
    /// <summary>Defines the metered linear middle-gray target.</summary>
    public float MiddleGray { get; set; } = .18f;
    /// <summary>Rejects the darkest weighted fraction of histogram samples.</summary>
    public float LowPercentile { get; set; } = .1f;
    /// <summary>Rejects the brightest weighted fraction of histogram samples.</summary>
    public float HighPercentile { get; set; } = .9f;
    /// <summary>Bounds adaptation toward brighter exposure in stops per second.</summary>
    public float BrightenRate { get; set; } = 4;
    /// <summary>Bounds adaptation toward darker exposure in stops per second.</summary>
    public float DarkenRate { get; set; } = 8;
    /// <summary>Weights the image center while retaining peripheral samples.</summary>
    public bool CenterWeighted { get; set; } = true;

    /// <summary>Produces a finite immutable snapshot for coherent frame inputs and change detection.</summary>
    internal CameraExposureParameters Snapshot() => new(Enabled,
        Clamp(Compensation, 0, -8, 8), ExposureCalibration.ToLogGain(Clamp(ManualEV100, 0, -24, 48)),
        ExposureCalibration.ToLogGain(Math.Max(Clamp(MinEV100, -2, -24, 48), Clamp(MaxEV100, 20, -24, 48))),
        ExposureCalibration.ToLogGain(Clamp(MinEV100, -2, -24, 48)),
        Clamp(MiddleGray, .18f, .01f, 1), Clamp(LowPercentile, .1f, 0, .49f),
        Clamp(HighPercentile, .9f, .51f, 1), Clamp(BrightenRate, 4, .01f, 20),
        Clamp(DarkenRate, 8, .01f, 20), CenterWeighted);
    #endregion
    #region Private
    /// <summary>Rejects non-finite persisted values before they reach GPU arithmetic.</summary>
    private static float Clamp(float value, float fallback, float min, float max)
        => Math.Clamp(float.IsFinite(value) ? value : fallback, min, max);
    #endregion
}
