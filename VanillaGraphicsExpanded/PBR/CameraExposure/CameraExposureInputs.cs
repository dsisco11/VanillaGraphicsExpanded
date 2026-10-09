using VanillaGraphicsExpanded.Rendering;
namespace VanillaGraphicsExpanded.PBR.CameraExposure;

/// <summary>Packs the shared histogram/adaptation std140 contract without GPU readback.</summary>
internal sealed class CameraExposureInputs : CpuUniformBuffer
{
    #region Public API
    /// <summary>Reserves four vec4 slots for metering, bounds, timing and percentile policy.</summary>
    internal CameraExposureInputs() : base(64) { }
    /// <summary>Stages one coherent settings snapshot and history validity for the submitted frame.</summary>
    internal void Capture(CameraExposureParameters settings, bool reset)
    {
        WriteVector4(0, new(-12, 16, settings.CenterWeighted ? 1 : 0, 0));
        WriteVector4(16, new(settings.MinEV, settings.MaxEV, settings.Compensation, settings.MiddleGray));
        WriteVector4(32, new(0, settings.BrightenRate, settings.DarkenRate, reset ? 1 : 0));
        WriteVector4(48, new(settings.LowPercentile, settings.HighPercentile, settings.ManualEV, settings.Enabled ? 1 : 0));
    }
    #endregion
}
