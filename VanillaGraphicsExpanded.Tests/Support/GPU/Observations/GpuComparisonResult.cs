namespace VanillaGraphicsExpanded.Tests.Support.GPU.Observations;

/// <summary>Contains complete-domain expected-value comparison facts.</summary>
internal class GpuComparisonResult(GpuObservationData data) : GpuFailureResult(data)
{
    #region Internal API
    /// <summary>Counts every selected expected-value component examined by the completed comparison.</summary>
    internal uint ComparedComponents => Data.Counters[19];
    #endregion
}
