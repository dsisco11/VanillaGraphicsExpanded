namespace VanillaGraphicsExpanded.Tests.Support.GPU.Observations;

/// <summary>Contains verified dispatch failure evidence without exposing uncomputed inspection or statistics facts.</summary>
internal class GpuFailureResult(GpuObservationData data)
{
    protected GpuObservationData Data { get; } = data;
    #region Public API
    /// <summary>Reports the deterministic earliest component failure in the selected domain.</summary>
    public override string ToString()
    {
        uint first = Data.Counters[6], texel = first / 4;
        string location = first == uint.MaxValue ? "none" : $"({texel % Data.Width},{texel / Data.Width % Data.Height},{texel / (Data.Width * Data.Height)}), channel={first % 4}";
        return $"domain={Data.Width}x{Data.Height}x{Data.Depth}, mismatches={Mismatches}, first={location}" + (Data.ExpectedActual is null ? string.Empty : $", {Data.ExpectedActual}");
    }
    #endregion
    #region Internal API
    /// <summary>Counts the exact dispatched texel domain whose completion was verified before result construction.</summary>
    internal uint VisitedTexels => Data.Counters[1];
    /// <summary>Counts independently detected expected-value or predicate failures.</summary>
    internal uint Mismatches => Data.Counters[2];
    /// <summary>Requires the exact expected-value or predicate contract.</summary>
    internal void AssertEqual() => Assert.True(Mismatches == 0, ToString());
    #endregion
}
