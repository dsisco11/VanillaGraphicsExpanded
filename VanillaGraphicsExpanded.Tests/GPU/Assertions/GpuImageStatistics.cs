using System.Numerics;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Rendering.RHI;

namespace VanillaGraphicsExpanded.Tests.GPU.Assertions;

/// <summary>Owns statistics observation policy with explicit fixture dependencies.</summary>
internal sealed partial class GpuImageStatistics : GpuObservationService
{
    private readonly GpuImageTransform projections;

    #region Internal API
    #region Dependencies
    /// <summary>Borrows prepared executables and related observation services from the fixture owner.</summary>
    internal GpuImageStatistics(GpuObservationPrograms programs, GpuImageTransform projections) : base(programs)
    {
        this.projections = projections;
    }

    #endregion

    #region Correlated counts
    /// <summary>Counts correlated texels exceeding a scaled baseline while a third image also exceeds its nominated threshold.</summary>
    internal GpuCountResult CountExcess(GpuTexture image, GpuTexture baseline, GpuTexture gate,
        float multiplier, float excess, float gateMinimum, float divisor = 1)
    {
        Programs.RequireContext();
        using var actual = new GpuObservationImage(image); using var reference = new GpuObservationImage(baseline);
        using var condition = new GpuObservationImage(gate);
        RequireMatchingFloating(actual, reference); RequireMatchingFloating(actual, condition);
        if (!float.IsFinite(multiplier) || !float.IsFinite(excess) || !float.IsFinite(gateMinimum) || !float.IsFinite(divisor) || divisor == 0)
            throw new ArgumentException("Correlated thresholds require finite scale and offset inputs.");
        var inputs = DomainInputs(actual, 1, 0, double.NegativeInfinity, double.PositiveInfinity);
        GpuAssertionInputs.Integer(inputs, 12, (uint)InspectionOperation.Excess);
        GpuAssertionInputs.Vector(inputs, 64, new(multiplier,excess,gateMinimum,divisor));
        return new(Observe(actual, "inspection", inputs, [("actualImage", actual.Texture), ("historyImage", reference.Texture), ("additionalImage", condition.Texture)]));
    }

    /// <summary>Counts texels satisfying all or any nominated channel intervals, retaining correlation between values and validity.</summary>
    internal GpuCountResult CountMatching(GpuTexture image, System.Numerics.Vector4 minimum,
        System.Numerics.Vector4 maximum, uint channelMask = 7, bool requireAll = true)
    {
        Programs.RequireContext();
        using var actual = new GpuObservationImage(image);
        if (actual.Kind != 0) throw new ArgumentException("Floating channel intervals require floating or normalized storage.");
        for (int channel = 0; channel < 4; channel++)
            if (float.IsNaN(minimum[channel]) || float.IsNaN(maximum[channel]) || minimum[channel] > maximum[channel])
                throw new ArgumentException("Channel intervals must be ordered and contain no NaN.");
        var inputs = DomainInputs(actual, channelMask, 0, double.NegativeInfinity, double.PositiveInfinity);
        GpuAssertionInputs.Integer(inputs, 12, (uint)InspectionOperation.ChannelIntervals);
        GpuAssertionInputs.Integer(inputs, 20, (uint)(requireAll ? 1u : 0u));
        for (int channel = 0; channel < 4; channel++) { GpuAssertionInputs.Integer(inputs, 96 + channel * 4, BitConverter.SingleToUInt32Bits(minimum[channel])); GpuAssertionInputs.Integer(inputs, 112 + channel * 4, BitConverter.SingleToUInt32Bits(maximum[channel])); }
        return new(Observe(actual, "inspection", inputs, [("actualImage", actual.Texture)]));
    }

    #endregion

    #region Moments and covered domains
    /// <summary>Measures the independent periodic Poisson equation residual without reproducing the solver's iteration algorithm.</summary>
    internal GpuStatisticsResult PeriodicLaplacianResidual(GpuTexture image, float[] rightHandSide)
    {
        Programs.RequireContext();
        using var actual = new GpuObservationImage(image);
        int count = checked(actual.Texture.Width * actual.Texture.Height);
        if (actual.Kind != 0 || actual.Channels != 1 || actual.Texture.Depth != 1 || rightHandSide.Length != count)
            throw new ArgumentException("A periodic residual requires one scalar floating plane and a complete authored right-hand side.");
        uint[] bits = new uint[checked(count * 4)];
        for (int pixel = 0; pixel < count; pixel++) bits[pixel * 4] = BitConverter.SingleToUInt32Bits(rightHandSide[pixel]);
        using var input = CreateArray(TextureFormat.Rgba32ui, actual.Texture.Width, actual.Texture.Height, 1);
        input.UploadDataImmediate(bits, 0, 0, 0, input.Width, input.Height, 1);
        var inputs = DomainInputs(actual, 1, 0, double.NegativeInfinity, double.PositiveInfinity);
        GpuAssertionInputs.Integer(inputs, 12, (uint)InspectionOperation.Moments);
        return new(Moments(actual, inputs, 1, [("actualImage", actual.Texture), ("expectedImage", input)]), 1);
    }

    /// <summary>Checks nominated values against their lit range where coverage is nonzero, and exact zero elsewhere.</summary>
    internal GpuInspectionResult InspectCoverage(GpuTexture image, double minimum, double maximum,
        int coverageChannel = 3, uint channelMask = 1)
    {
        Programs.RequireContext();
        using var actual = new GpuObservationImage(image);
        if (actual.Kind != 0 || (uint)coverageChannel >= (uint)actual.Channels ||
            double.IsNaN(minimum) || double.IsNaN(maximum) || minimum > maximum)
            throw new ArgumentException("Covered ranges require a represented floating mask and ordered bounds.");
        var inputs = DomainInputs(actual, channelMask, 0, minimum, maximum);
        GpuAssertionInputs.Integer(inputs, 12, (uint)InspectionOperation.CoveredRange);
        GpuAssertionInputs.Integer(inputs, 80, (uint)(coverageChannel));
        return new(Observe(actual, "inspection", inputs, [("actualImage", actual.Texture)]));
    }

    /// <summary>Returns complete-domain finite/extrema predicates and double sums and squared sums using bounded device reductions.</summary>
    internal GpuStatisticsResult Statistics(GpuTexture image, RHITextureRegion? selection = null, uint? channelMask = null, bool absolute = false, bool squareInFloat = false)
    {
        Programs.RequireContext();
        using var actual = new GpuObservationImage(image, selection);
        var inputs = DomainInputs(actual, channelMask, 0, double.NegativeInfinity, double.PositiveInfinity);
        GpuAssertionInputs.Integer(inputs, 12, (uint)InspectionOperation.Moments);
        var moments = new GpuStatisticsResult(Moments(actual, inputs, absolute ? 2 : 0, [("actualImage", actual.Texture)]), channelMask ?? (1u << actual.Channels) - 1);
        if(!squareInFloat)return moments;
        // Round products into explicit float storage before the independent double reduction.
        using var products=projections.Apply(actual.Texture, ObservationProjections.GpuImageAssertionsComparisons_Statistics);
        return moments.WithSquaredSums(Statistics(products,channelMask:channelMask??(1u<<actual.Channels)-1));
    }

    #endregion

    #region Classification and rank coverage
    /// <summary>Counts selected components carrying any nominated raw flag, without converting floating metadata to numbers.</summary>
    internal GpuFlagResult CountFlags(GpuTexture image, uint flags, uint channelMask)
    {
        Programs.RequireContext();
        using var actual = new GpuObservationImage(image);
        var inputs = DomainInputs(actual, channelMask, 0, double.NegativeInfinity, double.PositiveInfinity);
        GpuAssertionInputs.Integer(inputs, 12, (uint)InspectionOperation.Flags);
        GpuAssertionInputs.Integer(inputs, 20, (uint)(flags));
        return new(Observe(actual, "inspection", inputs, [("actualImage", actual.Texture)]));
    }

    /// <summary>Checks all selected texels for finite values, range, positive values and extrema.</summary>
    internal GpuInspectionResult Inspect(GpuTexture image, double minimum = double.NegativeInfinity,
        double maximum = double.PositiveInfinity, RHITextureRegion? selection = null, uint? channelMask = null,
        int pixelStride = 1, int pixelOffset = 0)
    {
        Programs.RequireContext();
        if (double.IsNaN(minimum) || double.IsNaN(maximum) || minimum > maximum) throw new ArgumentException("Invalid assertion range.");
        using var actual = new GpuObservationImage(image, selection);
        if (pixelStride <= 0 || pixelOffset < 0 || pixelOffset >= pixelStride || pixelOffset >= (long)actual.Texture.Width * actual.Texture.Height * actual.Texture.Depth)
            throw new ArgumentOutOfRangeException(nameof(pixelStride));
        var inputs = DomainInputs(actual, channelMask, 0, minimum, maximum);
        GpuAssertionInputs.Integer(inputs, 12, (uint)InspectionOperation.FiniteRange);
        GpuAssertionInputs.Integer(inputs, 84, (uint)(pixelStride));
        GpuAssertionInputs.Integer(inputs, 88, (uint)(pixelOffset));
        return new(Observe(actual, "inspection", inputs, [("actualImage", actual.Texture)]));
    }

    /// <summary>Validates every represented component and measures peak absolute RGB in one complete GPU observation.</summary>
    internal GpuRgbPeakInspectionResult InspectRgbPeak(GpuTexture image)
    {
        Programs.RequireContext();
        using var actual = new GpuObservationImage(image);
        if (actual.Kind != 0) throw new ArgumentException("RGB peak inspection requires floating or normalized storage.", nameof(image));
        if (actual.Channels < 3) throw new ArgumentOutOfRangeException(nameof(image), "RGB peak inspection requires three represented color channels.");
        var inputs = DomainInputs(actual, null, 0, double.NegativeInfinity, double.PositiveInfinity);
        GpuAssertionInputs.Integer(inputs, 12, (uint)InspectionOperation.RgbPeak);
        return new(Observe(actual, "inspection", inputs, [("actualImage", actual.Texture)]));
    }

    /// <summary>Checks every normalized rank in each vertically stacked square slice, including all slice differences.</summary>
    internal GpuRankResult CheckRanks(GpuTexture image, int side, int slices)
    {
        Programs.RequireContext();
        if (side <= 0 || slices is < 1 or > 32 || image.Width != side || image.Height != checked(side * slices) || image.Depth != 1)
            throw new ArgumentException("Rank storage must be vertically stacked square slices.");
        using var actual = new GpuObservationImage(image);
        if (actual.Kind != 0 || actual.Channels != 1) throw new ArgumentException("Normalized ranks require a scalar floating image.");
        var inputs = DomainInputs(actual, 1, 0, 0, 1);
        GpuAssertionInputs.Integer(inputs, 12, (uint)InspectionOperation.RankCounts);
        GpuAssertionInputs.Integer(inputs, 24, (uint)(side));
        GpuAssertionInputs.Integer(inputs, 28, (uint)(slices));
        return new(Ranks(actual, inputs, side, slices));
    }

    #endregion

    #region Weighted and ordered moments
    /// <summary>Integrates every selected image component against independent double-precision quadrature weights.</summary>
    internal GpuStatisticsResult WeightedStatistics(GpuTexture image,double[] weights,uint? channelMask=null,double[]? secondaryWeights=null)
    {
        Programs.RequireContext();using var actual=new GpuObservationImage(image);
        int count=checked(actual.Texture.Width*actual.Texture.Height*actual.Texture.Depth);
        if(actual.Kind!=0 || weights.Length!=count || (secondaryWeights is not null && secondaryWeights.Length!=count))
            throw new ArgumentException("Weighted moments require floating images and one weight per original texel.");
        uint[] bits=new uint[checked(count*4)];
        for(int pixel=0;pixel<count;pixel++)
        {
            if(!double.IsFinite(weights[pixel]))throw new ArgumentException("Quadrature weights must be finite.",nameof(weights));
            ulong value=BitConverter.DoubleToUInt64Bits(weights[pixel]);
            bits[pixel*4]=(uint)value;bits[pixel*4+1]=(uint)(value>>32);
            double secondary=secondaryWeights?[pixel]??1;
            if(!double.IsFinite(secondary))throw new ArgumentException("Secondary quadrature weights must be finite.",nameof(secondaryWeights));
            value=BitConverter.DoubleToUInt64Bits(secondary);
            bits[pixel*4+2]=(uint)value;bits[pixel*4+3]=(uint)(value>>32);
        }
        using var input=CreateArray(TextureFormat.Rgba32ui,actual.Texture.Width,actual.Texture.Height,actual.Texture.Depth);
        input.UploadDataImmediate(bits,0,0,0,input.Width,input.Height,input.Depth);
        var inputs = DomainInputs(actual, channelMask, 0, double.NegativeInfinity, double.PositiveInfinity);
        GpuAssertionInputs.Integer(inputs, 12, (uint)InspectionOperation.Moments);
        return new(Moments(actual, inputs, 3, [("actualImage", actual.Texture), ("expectedImage", input)]), channelMask ?? (1u << actual.Channels) - 1);
    }

    #endregion
    #endregion

    #region Private
    /// <summary>Identifies only the wire operations implemented by this focused observation service.</summary>
    private enum InspectionOperation
    {
        /// <summary>Selects the FiniteRange kernel operation.</summary>
        FiniteRange = 0,
        /// <summary>Selects all-channel finiteness with an RGB absolute peak.</summary>
        RgbPeak = 22,
        /// <summary>Selects the RankCounts kernel operation.</summary>
        RankCounts = 3,
        /// <summary>Selects the Flags kernel operation.</summary>
        Flags = 9,
        /// <summary>Selects the Moments kernel operation.</summary>
        Moments = 11,
        /// <summary>Selects the CoveredRange kernel operation.</summary>
        CoveredRange = 14,
        /// <summary>Selects the ChannelIntervals kernel operation.</summary>
        ChannelIntervals = 18,
        /// <summary>Selects the Excess kernel operation.</summary>
        Excess = 20,
    }
    #endregion
}
