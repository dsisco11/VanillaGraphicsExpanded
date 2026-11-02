namespace VanillaGraphicsExpanded.Tests.GPU.Assertions;

/// <summary>Contains comparison facts and proof of every nominated expected component.</summary>
internal sealed class GpuSparseComparisonResult(GpuObservationData data) : GpuInspectionResult(data)
{
    #region Internal API
    /// <summary>Counts the nominated components actually compared.</summary>
    internal uint ComparedComponents => Data.Counters[14];
    #endregion
}
