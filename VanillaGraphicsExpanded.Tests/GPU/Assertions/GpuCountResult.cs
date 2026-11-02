namespace VanillaGraphicsExpanded.Tests.GPU.Assertions;

/// <summary>Contains finite domain facts and a correlated texel count.</summary>
internal sealed class GpuCountResult(GpuObservationData data) : GpuInspectionResult(data)
{
    #region Internal API
    /// <summary>Counts complete texels satisfying the authored correlated channel contract.</summary>
    internal uint MatchingTexels => Data.Counters[14];
    #endregion
}
