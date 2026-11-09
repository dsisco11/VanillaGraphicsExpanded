namespace VanillaGraphicsExpanded.Tests.Support.GPU.Observations;

/// <summary>Retains full-domain inspection diagnostics and exposes the independently accumulated RGB peak magnitude.</summary>
internal sealed class GpuRgbPeakInspectionResult(GpuObservationData data) : GpuInspectionResult(data)
{
    #region Internal API
    /// <summary>Reports the greatest finite absolute value among represented red, green and blue components.</summary>
    internal float RgbPeakMagnitude => BitConverter.UInt32BitsToSingle(Data.Counters[16]);
    #endregion
}
