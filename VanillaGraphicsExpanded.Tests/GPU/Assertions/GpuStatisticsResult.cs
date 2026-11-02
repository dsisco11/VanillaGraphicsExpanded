namespace VanillaGraphicsExpanded.Tests.GPU.Assertions;

/// <summary>Contains completed double sums and squared sums, independently of other count result meanings.</summary>
internal sealed class GpuStatisticsResult : GpuInspectionResult
{
    #region Internal API
    /// <summary>Requires both partial and final reduction completion before exposing moments.</summary>
    internal GpuStatisticsResult(GpuObservationData data) : base(data)
    {
        if (data.Counters[11] != 256 || data.Counters[12] != 0x53554d53 || data.Counters[14] != 1)
            throw new InvalidOperationException("GPU statistics did not complete.");
    }
    /// <summary>Gets the selected channel's independently reduced double sum.</summary>
    internal double Sum(int channel) => Moment(channel, 16);
    /// <summary>Gets the selected channel's independently reduced double squared sum.</summary>
    internal double SumSquares(int channel) => Moment(channel, 24);
    /// <summary>Combines original facts with sums of separately rounded float products.</summary>
    internal GpuStatisticsResult WithSquaredSums(GpuStatisticsResult products)
    {
        if (products.Components != Components) throw new ArgumentException("Product moments must cover the same selected components.", nameof(products));
        uint[] combined = (uint[])Data.Counters.Clone();
        for (int channel = 0; channel < 4; channel++)
        {
            ulong bits = BitConverter.DoubleToUInt64Bits(products.Sum(channel));
            combined[24 + channel * 2] = (uint)bits; combined[25 + channel * 2] = (uint)(bits >> 32);
        }
        return new(Data with { Counters = combined });
    }
    #endregion
    #region Private
    /// <summary>Decodes original double bits without a floating intermediate.</summary>
    private double Moment(int channel, int offset)
    {
        if ((uint)channel >= 4) throw new ArgumentOutOfRangeException(nameof(channel));
        int index = offset + channel * 2;
        return BitConverter.Int64BitsToDouble(unchecked((long)(((ulong)Data.Counters[index + 1] << 32) | Data.Counters[index])));
    }
    #endregion
}
