namespace VanillaGraphicsExpanded.Tests.Support.GPU.Observations;

/// <summary>Contains independent predicate failures and explicitly nominated witness facts.</summary>
internal sealed class GpuPredicateResult(GpuObservationData data) : GpuFailureResult(data)
{
    #region Public API
    /// <summary>Includes the shader-executed request selector in failure diagnostics.</summary>
    public override string ToString() => base.ToString() + $", choice={Data.Counters[16]}, sourceMask=0x{Data.Counters[17]:x}";
    #endregion
    #region Internal API
    /// <summary>Counts texels satisfying the independently authored witness selection.</summary>
    internal uint MatchingTexels => Data.Counters[14];
    /// <summary>Returns the earliest witness only when an authored witness actually exists.</summary>
    internal int FirstWitnessIndex => MatchingTexels == 0 || Data.Counters[10] == uint.MaxValue
        ? throw new InvalidOperationException("No authored witness was selected.") : checked((int)Data.Counters[10]);
    #endregion
}
