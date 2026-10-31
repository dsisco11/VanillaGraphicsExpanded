namespace VanillaGraphicsExpanded.Tests.GPU.Assertions;

/// <summary>Contains bounded predicate counts and a location for the first failed scalar, independent of image size.</summary>
internal sealed class GpuImageResult(uint[] counters, int width, int height, int depth, int kind)
{
    #region Public API
    /// <summary>Reports counts and the deterministic earliest texel/channel failure, including its selected layer.</summary>
    public override string ToString()
    {
        uint first = counters[6], texel = first / 4;
        string location = first == uint.MaxValue ? "none" : $"({texel % width},{texel / width % height},{texel / (width * height)}), channel={first % 4}";
        string rank = counters[10] == uint.MaxValue ? "none" : $"rank={counters[10] % (width * width)}, slice={counters[10] / (width * width)}";
        return $"domain={width}x{height}x{depth}, mismatches={Mismatches}, nonfinite={NonFinite}, range={OutsideRange}, first={location}, histogram={rank}, min={Minimum:R}, max={Maximum:R}, differentSlices=0x{DifferentSlices:x}";
    }
    #endregion

    #region Internal API
    internal uint Mismatches => counters[2];
    internal uint NonFinite => counters[3];
    internal uint OutsideRange => counters[4];
    internal uint Positive => counters[5];
    internal uint DifferentSlices => counters[9];
    internal double Minimum => Decode(counters[7]);
    internal double Maximum => Decode(counters[8]);

    /// <summary>Requires the caller's exact expected-value contract without implicitly forbidding authored NaN/infinity values.</summary>
    internal void AssertEqual() => Assert.True(Mismatches == 0, ToString());
    /// <summary>Requires every selected component to be finite and inside the caller's explicit range.</summary>
    internal void AssertFiniteRange() => Assert.True(NonFinite == 0 && OutsideRange == 0, ToString());
    /// <summary>Requires each rank exactly once per slice and every noninitial slice to differ from the first.</summary>
    internal void AssertRanks(int slices)
    {
        AssertEqual(); AssertFiniteRange();
        uint required = slices == 32 ? uint.MaxValue - 1 : ((1u << slices) - 1) & ~1u;
        Assert.True(DifferentSlices == required, ToString());
    }
    #endregion



    #region Private
    /// <summary>Decodes ordered extrema without converting unsigned integer subjects to imprecise float values.</summary>
    private double Decode(uint ordered)
    {
        if (kind == 2) return ordered;
        if (kind == 1) return unchecked((int)(ordered ^ 0x80000000));
        uint bits = (ordered & 0x80000000) != 0 ? ordered ^ 0x80000000 : ~ordered;
        return BitConverter.UInt32BitsToSingle(bits);
    }
    #endregion
}
