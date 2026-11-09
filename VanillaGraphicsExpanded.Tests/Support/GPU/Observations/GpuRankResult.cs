namespace VanillaGraphicsExpanded.Tests.Support.GPU.Observations;

/// <summary>Contains completed permutation coverage and per-slice difference facts.</summary>
internal sealed class GpuRankResult(GpuObservationData data) : GpuInspectionResult(data)
{
    #region Public API
    /// <summary>Adds the earliest missing/duplicate rank to the original component diagnostics.</summary>
    public override string ToString() => base.ToString() + $", histogram={Data.Counters[10]}, differentSlices=0x{DifferentSlices:x}";
    #endregion
    #region Internal API
    /// <summary>Identifies every noninitial slice differing from the first slice.</summary>
    internal uint DifferentSlices => Data.Counters[9];
    /// <summary>Requires every rank once in every slice and distinct noninitial slices.</summary>
    internal void AssertRanks(int slices)
    {
        AssertEqual(); AssertFiniteRange();
        uint required = slices == 32 ? uint.MaxValue - 1 : ((1u << slices) - 1) & ~1u;
        Assert.True(DifferentSlices == required, ToString());
    }
    #endregion
}
