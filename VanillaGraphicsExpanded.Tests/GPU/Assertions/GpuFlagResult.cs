namespace VanillaGraphicsExpanded.Tests.GPU.Assertions;

/// <summary>Contains domain facts and a raw flag-match count.</summary>
internal sealed class GpuFlagResult(GpuObservationData data) : GpuInspectionResult(data)
{
    #region Internal API
    /// <summary>Counts selected raw components carrying the requested flags.</summary>
    internal uint FlagMatches => Data.Counters[14];
    #endregion
}
