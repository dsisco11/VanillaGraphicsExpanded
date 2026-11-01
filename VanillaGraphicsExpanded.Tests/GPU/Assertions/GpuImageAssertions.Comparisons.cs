using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Rendering.RHI;

namespace VanillaGraphicsExpanded.Tests.GPU.Assertions;

/// <summary>Compares complete image domains against independently authored references and retained histories.</summary>
internal sealed partial class GpuImageAssertions
{
    #region Internal API
    #region Correlated observations
    /// <summary>Checks an authored affine interval relative to a second image at every selected component.</summary>
    internal GpuImageResult CompareRelation(GpuTexture image, GpuTexture reference, float multiplier,
        float lowerOffset, float upperOffset, uint channelMask = 7, float divisor = 1)
    {
        RequireContext();
        using var actual = new GpuImageSnapshot(image); using var baseline = new GpuImageSnapshot(reference);
        RequireMatchingFloating(actual, baseline);
        if (float.IsNaN(multiplier) || float.IsNaN(lowerOffset) || float.IsNaN(upperOffset) || lowerOffset > upperOffset ||
            !float.IsFinite(divisor) || divisor == 0)
            throw new ArgumentException("Relative intervals must be ordered and contain no NaN.");
        return Run(actual, 19, null, baseline.Texture, 0, lowerOffset, upperOffset, channelMask,
            multipliers: new(multiplier,0,0,divisor));
    }

    /// <summary>Counts correlated texels exceeding a scaled baseline while a third image also exceeds its nominated threshold.</summary>
    internal GpuImageResult CountExcess(GpuTexture image, GpuTexture baseline, GpuTexture gate,
        float multiplier, float excess, float gateMinimum, float divisor = 1)
    {
        RequireContext();
        using var actual = new GpuImageSnapshot(image); using var reference = new GpuImageSnapshot(baseline);
        using var condition = new GpuImageSnapshot(gate);
        RequireMatchingFloating(actual, reference); RequireMatchingFloating(actual, condition);
        if (!float.IsFinite(multiplier) || !float.IsFinite(excess) || !float.IsFinite(gateMinimum) || !float.IsFinite(divisor) || divisor == 0)
            throw new ArgumentException("Correlated thresholds require finite scale and offset inputs.");
        return Run(actual, 20, null, reference.Texture, 0, double.NegativeInfinity, double.PositiveInfinity, 1,
            multipliers: new(multiplier,excess,gateMinimum,divisor), additional: condition.Texture);
    }

    /// <summary>Counts texels satisfying all or any nominated channel intervals, retaining correlation between values and validity.</summary>
    internal GpuImageResult CountMatching(GpuTexture image, System.Numerics.Vector4 minimum,
        System.Numerics.Vector4 maximum, uint channelMask = 7, bool requireAll = true)
    {
        RequireContext();
        using var actual = new GpuImageSnapshot(image);
        if (actual.Kind != 0) throw new ArgumentException("Floating channel intervals require floating or normalized storage.");
        for (int channel = 0; channel < 4; channel++)
            if (float.IsNaN(minimum[channel]) || float.IsNaN(maximum[channel]) || minimum[channel] > maximum[channel])
                throw new ArgumentException("Channel intervals must be ordered and contain no NaN.");
        return Run(actual, 18, null, null, 0, double.NegativeInfinity, double.PositiveInfinity, channelMask,
            constant: requireAll ? 1u : 0u, channelMinimum: minimum, channelMaximum: maximum);
    }

    #endregion
    #region Independent numerical references
    /// <summary>Measures the independent periodic Poisson equation residual without reproducing the solver's iteration algorithm.</summary>
    internal GpuImageResult PeriodicLaplacianResidual(GpuTexture image, float[] rightHandSide)
    {
        RequireContext();
        using var actual = new GpuImageSnapshot(image);
        int count = checked(actual.Texture.Width * actual.Texture.Height);
        if (actual.Kind != 0 || actual.Channels != 1 || actual.Texture.Depth != 1 || rightHandSide.Length != count)
            throw new ArgumentException("A periodic residual requires one scalar floating plane and a complete authored right-hand side.");
        uint[] bits = new uint[checked(count * 4)];
        for (int pixel = 0; pixel < count; pixel++) bits[pixel * 4] = BitConverter.SingleToUInt32Bits(rightHandSide[pixel]);
        using var input = CreateArray(TextureFormat.Rgba32ui, actual.Texture.Width, actual.Texture.Height, 1);
        input.UploadDataImmediate(bits, 0, 0, 0, input.Width, input.Height, 1);
        return Run(actual, 11, input, null, 0, double.NegativeInfinity, double.PositiveInfinity, 1, statisticExpression: 1);
    }

    /// <summary>Retains double-precision CPU expectations without narrowing their values before the original tolerance check.</summary>
    internal GpuImageResult CompareDouble(GpuTexture image, double[] expected, double tolerance,
        RHITextureRegion? selection = null, uint? channelMask = null)
    {
        RequireContext();
        using var actual = new GpuImageSnapshot(image, selection);
        int count = checked(actual.Texture.Width * actual.Texture.Height * actual.Texture.Depth);
        if (expected.Length != checked(count * actual.Channels) || expected.Any(value => !double.IsFinite(value)))
            throw new ArgumentException("Finite double expectations must cover the complete selected domain.", nameof(expected));
        using var input = CreateDoubleInput(actual, [expected]);
        return Run(actual, 15, input, null, tolerance, double.NegativeInfinity, double.PositiveInfinity, channelMask);
    }

    /// <summary>Checks independent per-component intervals, including intersections of multiple expectations at one coordinate.</summary>
    internal GpuImageResult CompareIntervals(GpuTexture image, double[] lower, double[] upper,
        RHITextureRegion? selection = null, uint? channelMask = null)
    {
        RequireContext();
        using var actual = new GpuImageSnapshot(image, selection);
        int count = checked(actual.Texture.Width * actual.Texture.Height * actual.Texture.Depth * actual.Channels);
        if (lower.Length != count || upper.Length != count)
            throw new ArgumentException("Interval expectations must cover the selected domain.");
        for (int index = 0; index < count; index++)
            if (double.IsNaN(lower[index]) || double.IsNaN(upper[index]) || lower[index] > upper[index])
                throw new ArgumentException("Expected intervals must be ordered and contain no NaN bounds.");
        using var input = CreateDoubleInput(actual, [lower, upper]);
        return Run(actual, 17, input, null, 0, double.NegativeInfinity, double.PositiveInfinity, channelMask);
    }

    /// <summary>Compares exactly the authored sparse RGB coordinates while retaining whole-domain completion evidence.</summary>
    internal GpuImageResult CompareSelected(GpuTexture image, IReadOnlyDictionary<int, System.Numerics.Vector3> expected,
        double tolerance)
    {
        RequireContext();
        using var actual = new GpuImageSnapshot(image);
        if (actual.Kind != 0 || actual.Channels < 3 || expected.Count == 0)
            throw new ArgumentException("Selected comparisons require authored floating RGB coordinates.", nameof(expected));
        int count = checked(actual.Texture.Width * actual.Texture.Height * actual.Texture.Depth);
        uint[] bits = new uint[checked(count * 4)];
        foreach (var (pixel, value) in expected)
        {
            if ((uint)pixel >= (uint)count) throw new ArgumentOutOfRangeException(nameof(expected));
            bits[pixel * 4] = BitConverter.SingleToUInt32Bits(value.X);
            bits[pixel * 4 + 1] = BitConverter.SingleToUInt32Bits(value.Y);
            bits[pixel * 4 + 2] = BitConverter.SingleToUInt32Bits(value.Z);
            bits[pixel * 4 + 3] = 7;
        }
        using var input = CreateArray(TextureFormat.Rgba32ui, actual.Texture.Width, actual.Texture.Height, actual.Texture.Depth);
        input.UploadDataImmediate(bits, 0, 0, 0, input.Width, input.Height, input.Depth);
        var result = Run(actual, 16, input, null, tolerance, double.NegativeInfinity, double.PositiveInfinity, 7);
        Assert.Equal(checked((uint)expected.Count * 3), result.ComparedComponents);
        return result;
    }

    #endregion
    #region Coverage and statistics
    /// <summary>Checks nominated values against their lit range where coverage is nonzero, and exact zero elsewhere.</summary>
    internal GpuImageResult InspectCoverage(GpuTexture image, double minimum, double maximum,
        int coverageChannel = 3, uint channelMask = 1)
    {
        RequireContext();
        using var actual = new GpuImageSnapshot(image);
        if (actual.Kind != 0 || (uint)coverageChannel >= (uint)actual.Channels ||
            double.IsNaN(minimum) || double.IsNaN(maximum) || minimum > maximum)
            throw new ArgumentException("Covered ranges require a represented floating mask and ordered bounds.");
        return Run(actual, 14, null, null, 0, minimum, maximum, channelMask, referenceChannel: coverageChannel);
    }

    /// <summary>Checks existing per-pixel channel ratios against an independently authored multiplier and absolute tolerance.</summary>
    internal GpuImageResult CompareChannelRatios(GpuTexture image, System.Numerics.Vector4 multipliers,
        double tolerance, int referenceChannel = 0, uint channelMask = 6)
    {
        RequireContext();
        using var actual = new GpuImageSnapshot(image);
        if (actual.Kind != 0 || (uint)referenceChannel >= (uint)actual.Channels)
            throw new ArgumentException("Channel ratios require a represented floating reference channel.");
        return Run(actual, 13, null, null, tolerance, double.NegativeInfinity, double.PositiveInfinity,
            channelMask, multipliers: multipliers, referenceChannel: referenceChannel);
    }

    /// <summary>Returns complete-domain finite/extrema predicates and double sums and squared sums using bounded device reductions.</summary>
    internal GpuImageResult Statistics(GpuTexture image, RHITextureRegion? selection = null, uint? channelMask = null, bool absolute = false, bool squareInFloat = false)
    {
        RequireContext();
        using var actual = new GpuImageSnapshot(image, selection);
        var moments=Run(actual, 11, null, null, 0, double.NegativeInfinity, double.PositiveInfinity, channelMask,
            statisticExpression: absolute ? 2 : 0);
        if(!squareInFloat)return moments;
        // Round products into explicit float storage before the independent double reduction.
        using var products=(projections??=new()).Apply(actual.Texture,"precise vec4 square=a*a; return square;");
        return moments.WithSquaredSums(Statistics(products,channelMask:channelMask??(1u<<actual.Channels)-1));
    }

    /// <summary>Counts selected components carrying any nominated raw flag, without converting floating metadata to numbers.</summary>
    internal GpuImageResult CountFlags(GpuTexture image, uint flags, uint channelMask)
    {
        RequireContext();
        using var actual = new GpuImageSnapshot(image);
        return Run(actual, 9, null, null, 0, double.NegativeInfinity, double.PositiveInfinity, channelMask, constant: flags);
    }

    #endregion
    #region Direct expectations
    /// <summary>Checks every stencil value in the original selected domain without downloading its packed depth/stencil pixels.</summary>
    internal GpuImageResult CompareStencil(GpuTexture image,byte expected,RHITextureRegion? selection=null)
    {
        RequireContext();
        using var actual=new GpuImageSnapshot(image,selection,stencil:true);
        return Run(actual,5,null,null,0,0,255,1,constant:expected);
    }
    /// <summary>Compares every selected component with one authored floating constant without uploading an image-sized expectation.</summary>
    internal GpuImageResult CompareConstant(GpuTexture image, float expected, RHITextureRegion? selection = null,
        uint? channelMask = null, double tolerance = 0)
    {
        RequireContext();
        using var actual = new GpuImageSnapshot(image, selection);
        if (actual.Kind != 0) throw new ArgumentException("Floating constants require floating or normalized storage.", nameof(image));
        return Run(actual, 5, null, null, tolerance, double.NegativeInfinity, double.PositiveInfinity, channelMask,
            constant: BitConverter.SingleToUInt32Bits(expected));
    }

    /// <summary>Compares every selected channel against independent CPU IEEE/integer bit patterns.</summary>
    internal GpuImageResult CompareBits(GpuTexture image, uint[] expected, double tolerance = 0,
        RHITextureRegion? selection = null, uint? channelMask = null, bool vectorDistance = false, GpuTexture? additiveLayers = null,bool bitwise=false)
    {
        RequireContext();
        using var actual = new GpuImageSnapshot(image, selection);
        int count = checked(actual.Texture.Width * actual.Texture.Height * actual.Texture.Depth);
        if (expected.Length != checked(count * actual.Channels)) throw new ArgumentException("Expected data must cover the entire selected domain.", nameof(expected));
        uint[] expanded = new uint[checked(count * 4)];
        for (int pixel = 0; pixel < count; pixel++)
            Array.Copy(expected, pixel * actual.Channels, expanded, pixel * 4, actual.Channels);
        using var input = CreateArray(TextureFormat.Rgba32ui, actual.Texture.Width, actual.Texture.Height, actual.Texture.Depth);
        input.UploadDataImmediate(expanded, 0, 0, 0, input.Width, input.Height, input.Depth);
        using var layers = additiveLayers is null ? null : new GpuImageSnapshot(additiveLayers);
        if (layers is not null && (actual.Kind != 0 || layers.Kind != 0 || actual.Texture.Depth != 1 ||
            layers.Texture.Depth != 3 || layers.Texture.Width != actual.Texture.Width || layers.Texture.Height != actual.Texture.Height))
            throw new ArgumentException("Additive expectations require three matching floating layers and one actual image.");
        if (vectorDistance && (actual.Kind != 0 || actual.Channels < 3)) throw new ArgumentException("Vector distance needs floating RGB.");
        if (layers is not null && vectorDistance) throw new ArgumentException("Select a scalar or vector comparison independently.");
        if(bitwise && (vectorDistance || layers is not null || tolerance!=0))throw new ArgumentException("Bitwise expectations do not have a numeric tolerance or derived expectation.");
        if(bitwise && actual.Kind==0)return Run(actual,21,input,null,0,double.NegativeInfinity,double.PositiveInfinity,channelMask,predicate:"""
            uvec4 actual=floatBitsToUint(a),expected=texelFetch(expectedImage,coordinate,0);
            bool valid=true;
            for(int channel=0;channel<4;channel++)if((channels&(1u<<channel))!=0u)valid=valid && actual[channel]==expected[channel];
            return bvec2(valid,false);
            """);
        return Run(actual, layers is not null ? 10 : vectorDistance ? 8 : 1, input, layers?.Texture,
            tolerance, double.NegativeInfinity, double.PositiveInfinity, channelMask);
    }

    /// <summary>Compares independent floating expectations without losing signed zero, NaN or subnormal representations.</summary>
    internal GpuImageResult Compare(GpuTexture image, float[] expected, double tolerance = 0,
        RHITextureRegion? selection = null, uint? channelMask = null, bool vectorDistance = false, GpuTexture? additiveLayers = null,bool bitwise=false)
    {
        var bits = Array.ConvertAll(expected, BitConverter.SingleToUInt32Bits);
        return CompareBits(image, bits, tolerance, selection, channelMask, vectorDistance, additiveLayers,bitwise);
    }

    /// <summary>Checks all selected texels for finite values, range, positive values and extrema.</summary>
    internal GpuImageResult Inspect(GpuTexture image, double minimum = double.NegativeInfinity,
        double maximum = double.PositiveInfinity, RHITextureRegion? selection = null, uint? channelMask = null,
        int pixelStride = 1, int pixelOffset = 0)
    {
        RequireContext();
        if (double.IsNaN(minimum) || double.IsNaN(maximum) || minimum > maximum) throw new ArgumentException("Invalid assertion range.");
        using var actual = new GpuImageSnapshot(image, selection);
        return Run(actual, 0, null, null, 0, minimum, maximum, channelMask, pixelStride: pixelStride, pixelOffset: pixelOffset);
    }

    #endregion
    #region Histories and permutations
    /// <summary>Compares the complete current selection with an explicitly owned previous GPU snapshot.</summary>
    internal GpuImageResult CompareHistory(GpuTexture image, GpuImageSnapshot previous,
        RHITextureRegion? selection = null, double tolerance = 0, uint? channelMask = null,
        bool bitwise = false, bool retainNonzero = false)
    {
        RequireContext();
        using var actual = new GpuImageSnapshot(image, selection);
        previous.Texture.RequireOriginalContext();
        uint comparedChannels = channelMask ?? (1u << actual.Channels) - 1;
        if (actual.Kind != previous.Kind || (comparedChannels & ~((1u << previous.Channels) - 1)) != 0
            || actual.Texture.Width != previous.Texture.Width || actual.Texture.Height != previous.Texture.Height || actual.Texture.Depth != previous.Texture.Depth)
            throw new ArgumentException("History must retain the same selected domain and numeric representation.");
        if (bitwise && retainNonzero) throw new ArgumentException("Select either bitwise history or nonzero-history retention.");
        return Run(actual, bitwise ? 6 : retainNonzero ? 7 : 2, null, previous.Texture, tolerance,
            double.NegativeInfinity, double.PositiveInfinity, channelMask);
    }

    /// <summary>Checks every normalized rank in each vertically stacked square slice, including all slice differences.</summary>
    internal GpuImageResult CheckRanks(GpuTexture image, int side, int slices)
    {
        RequireContext();
        if (side <= 0 || slices is < 1 or > 32 || image.Width != side || image.Height != checked(side * slices) || image.Depth != 1)
            throw new ArgumentException("Rank storage must be vertically stacked square slices.");
        using var actual = new GpuImageSnapshot(image);
        if (actual.Kind != 0 || actual.Channels != 1) throw new ArgumentException("Normalized ranks require a scalar floating image.");
        return Run(actual, 3, null, null, 0, 0, 1, 1, side, slices);
    }
    #endregion
    #endregion

}
