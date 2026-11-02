namespace VanillaGraphicsExpanded.Tests.GPU.Assertions;

/// <summary>Contains complete-domain finite, range and extrema facts with bounded failure diagnostics.</summary>
internal class GpuInspectionResult(GpuObservationData data) : GpuFailureResult(data)
{
    #region Public API
    /// <summary>Reports the deterministic earliest component failure in the selected domain.</summary>
    public override string ToString() => base.ToString() + $", nonfinite={NonFinite}, range={OutsideRange}, min={Minimum:R}, max={Maximum:R}";
    #endregion
    #region Internal API
    /// <summary>Counts nonfinite represented components.</summary>
    internal uint NonFinite => Data.Counters[3];
    /// <summary>Counts finite components outside the authored bounds.</summary>
    internal uint OutsideRange => Data.Counters[4];
    /// <summary>Counts strictly positive represented components.</summary>
    internal uint Positive => Data.Counters[5];
    /// <summary>Counts nonzero finite represented components.</summary>
    internal uint Nonzero => Data.Counters[13];
    /// <summary>Counts every visited selected component.</summary>
    internal uint Components => Data.Counters[15];
    /// <summary>Counts finite represented components within the inclusive bounds.</summary>
    internal uint InsideRange => Components - NonFinite - OutsideRange;
    /// <summary>Reports the minimum in the subject's original numeric kind.</summary>
    internal double Minimum => Decode(Data.Counters[7]);
    /// <summary>Reports the maximum in the subject's original numeric kind.</summary>
    internal double Maximum => Decode(Data.Counters[8]);
    /// <summary>Requires finite components inside the explicit inclusive range.</summary>
    internal void AssertFiniteRange() => Assert.True(NonFinite == 0 && OutsideRange == 0, ToString());
    #endregion
    #region Private
    /// <summary>Decodes ordered extrema without rounding integer subjects through float.</summary>
    private double Decode(uint ordered)
    {
        if (Data.Kind == 2) return ordered;
        if (Data.Kind == 1) return unchecked((int)(ordered ^ 0x80000000));
        uint bits = (ordered & 0x80000000) != 0 ? ordered ^ 0x80000000 : ~ordered;
        return BitConverter.UInt32BitsToSingle(bits);
    }
    #endregion
}
