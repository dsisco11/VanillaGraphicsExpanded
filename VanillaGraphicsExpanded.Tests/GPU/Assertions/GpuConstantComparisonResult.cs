namespace VanillaGraphicsExpanded.Tests.GPU.Assertions;

/// <summary>Contains comparison facts and the number of authored constant matches.</summary>
internal sealed class GpuConstantComparisonResult(GpuObservationData data) : GpuInspectionResult(data)
{
    #region Internal API
    /// <summary>Counts components equal to the explicitly supplied constant.</summary>
    internal uint ConstantMatches => Data.Counters[14];
    /// <summary>Counts every represented selected component compared against the constant.</summary>
    internal uint ComparedComponents => Data.Counters[19];
    #endregion
}
