namespace VanillaGraphicsExpanded.Tests.GPU.Assertions;

/// <summary>Contains bounded predicate counts and a location for the first failed scalar, independent of image size.</summary>
internal sealed class GpuImageResult(uint[] counters, int width, int height, int depth, int kind,int operation=0)
{
    #region Public API
    /// <summary>Reports counts and the deterministic earliest texel/channel failure, including its selected layer.</summary>
    public override string ToString()
    {
        uint first = counters[6], texel = first / 4;
        string location = first == uint.MaxValue ? "none" : $"({texel % width},{texel / width % height},{texel / (width * height)}), channel={first % 4}";
        string rank = operation!=3 || counters[10] == uint.MaxValue ? "none" : $"rank={counters[10] % (width * width)}, slice={counters[10] / (width * width)}";
        return $"domain={width}x{height}x{depth}, mismatches={Mismatches}, nonfinite={NonFinite}, range={OutsideRange}, first={location}, histogram={rank}, min={Minimum:R}, max={Maximum:R}, differentSlices=0x{DifferentSlices:x}";
    }
    #endregion

    #region Internal API
    internal uint Mismatches => counters[2];
    internal uint NonFinite => counters[3];
    internal uint OutsideRange => counters[4];
    internal uint Positive => counters[5];
    /// <summary>Counts finite components unequal to numeric zero in the selected channels.</summary>
    internal uint Nonzero => counters[13];
    /// <summary>Counts components equal to the authored constant, using the explicit numeric equality policy.</summary>
    internal uint ConstantMatches => counters[14];
    /// <summary>Counts flag matches when the requested operation interprets selected components as bit fields.</summary>
    internal uint FlagMatches => counters[14];
    /// <summary>Counts the explicitly selected expected-value components actually compared by a sparse operation.</summary>
    internal uint ComparedComponents => counters[14];
    /// <summary>Counts complete texels satisfying the requested correlated channel predicate.</summary>
    internal uint MatchingTexels => counters[14];
    /// <summary>Gets the earliest authored witness in original texel order; absent witnesses cannot invent a coordinate.</summary>
    internal int FirstWitnessIndex
    {
        get
        {
            if(operation!=21 || MatchingTexels==0 || counters[10]==uint.MaxValue)throw new InvalidOperationException("No authored witness was selected.");
            return checked((int)counters[10]);
        }
    }
    /// <summary>Counts every selected component, retaining explicit channel-mask and complete-domain coverage.</summary>
    internal uint Components => counters[15];
    /// <summary>Counts finite selected values inside the inclusive authored range.</summary>
    internal uint InsideRange => Components - NonFinite - OutsideRange;
    /// <summary>Gets a selected channel's double sum after the independently verified statistics passes.</summary>
    internal double Sum(int channel) => Moment(channel, 16);
    /// <summary>Gets a selected channel's double squared sum without returning an image-sized CPU buffer.</summary>
    internal double SumSquares(int channel) => Moment(channel, 24);
    /// <summary>Combines proved original-image observations with proved sums of separately rounded float products.</summary>
    internal GpuImageResult WithSquaredSums(GpuImageResult products)
    {
        _=Sum(0);
        if(products.Components!=Components)throw new ArgumentException("Product moments must cover the same selected components.",nameof(products));
        uint[] combined=(uint[])counters.Clone();
        for(int channel=0;channel<4;channel++)
        {
            ulong bits=BitConverter.DoubleToUInt64Bits(products.Sum(channel));
            combined[24+channel*2]=(uint)bits;combined[25+channel*2]=(uint)(bits>>32);
        }
        return new(combined,width,height,depth,kind,operation);
    }
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
    /// <summary>Decodes one double result only when the statistics completion proof was delivered.</summary>
    private double Moment(int channel, int offset)
    {
        if ((uint)channel >= 4) throw new ArgumentOutOfRangeException(nameof(channel));
        if (counters[11] != 256 || counters[12] != 0x53554d53 || counters[14] != 1)
            throw new InvalidOperationException("This observation did not request complete image statistics.");
        int index = offset + channel * 2;
        return BitConverter.Int64BitsToDouble(unchecked((long)(((ulong)counters[index + 1] << 32) | counters[index])));
    }

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
