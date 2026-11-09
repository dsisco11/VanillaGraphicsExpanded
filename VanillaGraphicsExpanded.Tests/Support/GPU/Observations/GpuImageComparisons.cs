using System.Numerics;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Rendering.RHI;

namespace VanillaGraphicsExpanded.Tests.Support.GPU.Observations;

/// <summary>Owns comparisons observation policy with explicit fixture dependencies.</summary>
internal sealed partial class GpuImageComparisons : GpuObservationService
{

    #region Internal API
    #region Dependencies
    /// <summary>Borrows prepared executables and related observation services from the fixture owner.</summary>
    internal GpuImageComparisons(GpuObservationPrograms programs) : base(programs)
    {
    }

    #endregion

    #region Correlated intervals
    /// <summary>Checks an authored affine interval relative to a second image at every selected component.</summary>
    internal GpuComparisonResult CompareRelation(GpuTexture image, GpuTexture reference, float multiplier,
        float lowerOffset, float upperOffset, uint channelMask = 7, float divisor = 1)
    {
        Programs.RequireContext();
        using var actual = new GpuObservationImage(image); using var baseline = new GpuObservationImage(reference);
        RequireMatchingFloating(actual, baseline);
        if (float.IsNaN(multiplier) || float.IsNaN(lowerOffset) || float.IsNaN(upperOffset) || lowerOffset > upperOffset ||
            !float.IsFinite(divisor) || divisor == 0)
            throw new ArgumentException("Relative intervals must be ordered and contain no NaN.");
        var inputs = DomainInputs(actual, channelMask, 0, lowerOffset, upperOffset);
        GpuAssertionInputs.Integer(inputs, 12, (uint)ComparisonOperation.AffineInterval);
        GpuAssertionInputs.Vector(inputs, 64, new(multiplier,0,0,divisor));
        return new(CompareDomain(actual, "compare", inputs, [("actualImage", actual.Texture), ("historyImage", baseline.Texture)], (pixel, channel, value) => $"{FormatTuple(ReadDiagnosticTuple(baseline.Texture, pixel, baseline.Kind), baseline.Kind)} * {multiplier:R} / {divisor:R} + [{lowerOffset:R},{upperOffset:R}]"));
    }

    #endregion

    #region Independent numerical references
    /// <summary>Retains double-precision CPU expectations without narrowing their values before the original tolerance check.</summary>
    internal GpuComparisonResult CompareDouble(GpuTexture image, double[] expected, double tolerance,
        RHITextureRegion? selection = null, uint? channelMask = null)
    {
        Programs.RequireContext();
        using var actual = new GpuObservationImage(image, selection);
        int count = checked(actual.Texture.Width * actual.Texture.Height * actual.Texture.Depth);
        if (expected.Length != checked(count * actual.Channels) || expected.Any(value => !double.IsFinite(value)))
            throw new ArgumentException("Finite double expectations must cover the complete selected domain.", nameof(expected));
        using var input = CreateDoubleInput(actual.Channels, [expected], channelMask);
        var inputs = DomainInputs(actual, channelMask, tolerance, double.NegativeInfinity, double.PositiveInfinity);
        GpuAssertionInputs.Integer(inputs, 12, (uint)ComparisonOperation.DoubleExpected);
        GpuAssertionInputs.Integer(inputs, 80, (uint)BitOperations.PopCount(channelMask ?? ((1u << actual.Channels) - 1)));
        return new(CompareDomain(actual, "compare", inputs, [("actualImage", actual.Texture), ("expectedImage", input)], (pixel, channel, value) => expected[pixel * actual.Channels + channel].ToString("R")));
    }

    /// <summary>Checks independent per-component intervals, including intersections of multiple expectations at one coordinate.</summary>
    internal GpuComparisonResult CompareIntervals(GpuTexture image, double[] lower, double[] upper,
        RHITextureRegion? selection = null, uint? channelMask = null)
    {
        Programs.RequireContext();
        using var actual = new GpuObservationImage(image, selection);
        int count = checked(actual.Texture.Width * actual.Texture.Height * actual.Texture.Depth * actual.Channels);
        if (lower.Length != count || upper.Length != count)
            throw new ArgumentException("Interval expectations must cover the selected domain.");
        for (int index = 0; index < count; index++)
            if (double.IsNaN(lower[index]) || double.IsNaN(upper[index]) || lower[index] > upper[index])
                throw new ArgumentException("Expected intervals must be ordered and contain no NaN bounds.");
        using var input = CreateDoubleInput(actual.Channels, [lower, upper], channelMask);
        var inputs = DomainInputs(actual, channelMask, 0, double.NegativeInfinity, double.PositiveInfinity);
        GpuAssertionInputs.Integer(inputs, 12, (uint)ComparisonOperation.Intervals);
        GpuAssertionInputs.Integer(inputs, 80, (uint)BitOperations.PopCount(channelMask ?? ((1u << actual.Channels) - 1)));
        return new(CompareDomain(actual, "compare", inputs, [("actualImage", actual.Texture), ("expectedImage", input)], (pixel, channel, value) => $"[{lower[pixel * actual.Channels + channel]:R},{upper[pixel * actual.Channels + channel]:R}]"));
    }

    #endregion

    #region Authored channel expectations
    /// <summary>Checks existing per-pixel channel ratios against an independently authored multiplier and absolute tolerance.</summary>
    internal GpuComparisonResult CompareChannelRatios(GpuTexture image, System.Numerics.Vector4 multipliers,
        double tolerance, int referenceChannel = 0, uint channelMask = 6)
    {
        Programs.RequireContext();
        using var actual = new GpuObservationImage(image);
        if (actual.Kind != 0 || (uint)referenceChannel >= (uint)actual.Channels)
            throw new ArgumentException("Channel ratios require a represented floating reference channel.");
        var inputs = DomainInputs(actual, channelMask, tolerance, double.NegativeInfinity, double.PositiveInfinity);
        GpuAssertionInputs.Integer(inputs, 12, (uint)ComparisonOperation.ChannelRatio);
        GpuAssertionInputs.Integer(inputs, 80, (uint)(referenceChannel));
        GpuAssertionInputs.Vector(inputs, 64, multipliers);
        return new(CompareDomain(actual, "compare", inputs, [("actualImage", actual.Texture)], (pixel, channel, value) => (BitConverter.UInt32BitsToSingle(value[referenceChannel]) * multipliers[channel]).ToString("R")));
    }

    /// <summary>Checks every stencil value in the original selected domain without downloading its packed depth/stencil pixels.</summary>
    internal GpuConstantComparisonResult CompareStencil(GpuTexture image,byte expected,RHITextureRegion? selection=null)
    {
        Programs.RequireContext();
        using var actual=new GpuObservationImage(image,selection,stencil:true);
        var inputs = DomainInputs(actual, 1, 0, 0, 255);
        GpuAssertionInputs.Integer(inputs, 12, (uint)ComparisonOperation.Constant);
        GpuAssertionInputs.Integer(inputs, 20, (uint)(expected));
        return new(CompareDomain(actual, "compare", inputs, [("actualImage", actual.Texture)], (pixel, channel, value) => $"{expected}/0x{expected:x2}"));
    }

    /// <summary>Compares every selected component with one authored floating constant without uploading an image-sized expectation.</summary>
    internal GpuConstantComparisonResult CompareConstant(GpuTexture image, float expected, RHITextureRegion? selection = null,
        uint? channelMask = null, double tolerance = 0)
    {
        Programs.RequireContext();
        using var actual = new GpuObservationImage(image, selection);
        if (actual.Kind != 0) throw new ArgumentException("Floating constants require floating or normalized storage.", nameof(image));
        var inputs = DomainInputs(actual, channelMask, tolerance, double.NegativeInfinity, double.PositiveInfinity);
        GpuAssertionInputs.Integer(inputs, 12, (uint)ComparisonOperation.Constant);
        GpuAssertionInputs.Integer(inputs, 20, (uint)(BitConverter.SingleToUInt32Bits(expected)));
        return new(CompareDomain(actual, "compare", inputs, [("actualImage", actual.Texture)], (pixel, channel, value) => $"{expected:R}/0x{BitConverter.SingleToUInt32Bits(expected):x8}"));
    }

    #endregion

    #region Represented component equality
    /// <summary>Compares every selected channel against independent CPU IEEE/integer bit patterns.</summary>
    internal GpuComparisonResult CompareBits(GpuTexture image, uint[] expected, double tolerance = 0,
        RHITextureRegion? selection = null, uint? channelMask = null, bool vectorDistance = false, GpuTexture? additiveLayers = null,bool bitwise=false)
    {
        Programs.RequireContext();
        using var actual = new GpuObservationImage(image, selection);
        int count = checked(actual.Texture.Width * actual.Texture.Height * actual.Texture.Depth);
        if (expected.Length != checked(count * actual.Channels)) throw new ArgumentException("Expected data must cover the entire selected domain.", nameof(expected));
        using var input = CreateWords(SelectedWords(expected, actual.Channels, vectorDistance ? 7u : channelMask));
        using var layers = additiveLayers is null ? null : new GpuObservationImage(additiveLayers);
        if (layers is not null && (actual.Kind != 0 || layers.Kind != 0 || actual.Texture.Depth != 1 ||
            layers.Texture.Depth != 3 || layers.Texture.Width != actual.Texture.Width || layers.Texture.Height != actual.Texture.Height))
            throw new ArgumentException("Additive expectations require three matching floating layers and one actual image.");
        if (vectorDistance && (actual.Kind != 0 || actual.Channels < 3)) throw new ArgumentException("Vector distance needs floating RGB.");
        if (layers is not null && vectorDistance) throw new ArgumentException("Select a scalar or vector comparison independently.");
        if(bitwise && (vectorDistance || layers is not null || tolerance!=0))throw new ArgumentException("Bitwise expectations do not have a numeric tolerance or derived expectation.");
        var inputs = DomainInputs(actual, channelMask, tolerance, double.NegativeInfinity, double.PositiveInfinity);
        GpuAssertionInputs.Integer(inputs, 12, (uint)(bitwise && actual.Kind == 0 ? ComparisonOperation.ExactBits : layers is not null ? ComparisonOperation.AdditiveLayers : vectorDistance ? ComparisonOperation.VectorDistance : ComparisonOperation.ExpectedBits));
        GpuAssertionInputs.Integer(inputs, 80, (uint)BitOperations.PopCount(vectorDistance ? 7u : channelMask ?? ((1u << actual.Channels) - 1)));
        return new(CompareDomain(actual, "compare", inputs, [("actualImage", actual.Texture), ("historyImage", layers?.Texture), ("expectedImage", input)], (pixel, channel, value) => FormatTuple(expected.Skip(pixel * actual.Channels).Take(actual.Channels), actual.Kind) + (layers is null ? string.Empty : " + retained additive layers")));
    }

    /// <summary>Compares independent floating expectations without losing signed zero, NaN or subnormal representations.</summary>
    internal GpuComparisonResult Compare(GpuTexture image, float[] expected, double tolerance = 0,
        RHITextureRegion? selection = null, uint? channelMask = null, bool vectorDistance = false, GpuTexture? additiveLayers = null,bool bitwise=false)
    {
        var bits = Array.ConvertAll(expected, BitConverter.SingleToUInt32Bits);
        return CompareBits(image, bits, tolerance, selection, channelMask, vectorDistance, additiveLayers,bitwise);
    }

    #endregion

    #region History and typed references
    /// <summary>Compares the complete current selection with an explicitly owned previous GPU snapshot.</summary>
    internal GpuComparisonResult CompareHistory(GpuTexture image, GpuImageSnapshot previous,
        RHITextureRegion? selection = null, double tolerance = 0, uint? channelMask = null,
        bool bitwise = false, bool retainNonzero = false)
    {
        Programs.RequireContext();
        using var actual = new GpuObservationImage(image, selection);
        previous.Texture.RequireOriginalContext();
        uint comparedChannels = channelMask ?? (1u << actual.Channels) - 1;
        if (actual.Kind != previous.Kind || (comparedChannels & ~((1u << previous.Channels) - 1)) != 0
            || actual.Texture.Width != previous.Texture.Width || actual.Texture.Height != previous.Texture.Height || actual.Texture.Depth != previous.Texture.Depth)
            throw new ArgumentException("History must retain the same selected domain and numeric representation.");
        if (bitwise && retainNonzero) throw new ArgumentException("Select either bitwise history or nonzero-history retention.");
        var inputs = DomainInputs(actual, channelMask, tolerance, double.NegativeInfinity, double.PositiveInfinity);
        GpuAssertionInputs.Integer(inputs, 12, (uint)(bitwise ? ComparisonOperation.BitwiseHistory : retainNonzero ? ComparisonOperation.RetainNonzeroHistory : ComparisonOperation.History));
        return new(CompareDomain(actual, "compare", inputs, [("actualImage", actual.Texture), ("historyImage", previous.Texture)], (pixel, channel, value) => FormatTuple(ReadDiagnosticTuple(previous.Texture, pixel, previous.Kind), previous.Kind)));
    }

    /// <summary>Compares every RGBA8 metadata byte with independently authored input after exact normalized-byte reconstruction.</summary>
    internal GpuComparisonResult CompareRgba8(GpuTexture image,byte[] expected)
    {
        Programs.RequireContext();
        if(image.InternalFormat!=TextureFormat.Rgba8)throw new ArgumentException("Byte metadata comparison requires RGBA8 storage.",nameof(image));
        using var actual = new GpuObservationImage(image);
        if (expected.Length != checked(image.Width * image.Height * image.Depth * 4))
            throw new ArgumentException("Byte expectations must cover every RGBA8 component.", nameof(expected));
        using var input = CreateArray(TextureFormat.Rgba8ui, image.Width, image.Height, image.Depth);
        input.UploadDataImmediate(expected, 0, 0, 0, input.Width, input.Height, input.Depth);
        UploadedExpectationBytes += expected.Length;
        var inputs = DomainInputs(actual, 15, 0, double.NegativeInfinity, double.PositiveInfinity);
        GpuAssertionInputs.Integer(inputs, 12, (uint)ComparisonOperation.NormalizedBytes);
        return new(CompareDomain(actual, "compare", inputs, [("actualImage", actual.Texture), ("expectedImage", input)],
            (pixel, channel, value) => expected[pixel * 4 + channel].ToString()));
    }

    #endregion
    #endregion

    #region Private
    /// <summary>Identifies only the wire operations implemented by this focused observation service.</summary>
    private enum ComparisonOperation
    {
        /// <summary>Selects the ExpectedBits kernel operation.</summary>
        ExpectedBits = 1,
        /// <summary>Selects the History kernel operation.</summary>
        History = 2,
        /// <summary>Selects the Constant kernel operation.</summary>
        Constant = 5,
        /// <summary>Selects the BitwiseHistory kernel operation.</summary>
        BitwiseHistory = 6,
        /// <summary>Selects the RetainNonzeroHistory kernel operation.</summary>
        RetainNonzeroHistory = 7,
        /// <summary>Selects the VectorDistance kernel operation.</summary>
        VectorDistance = 8,
        /// <summary>Selects the AdditiveLayers kernel operation.</summary>
        AdditiveLayers = 10,
        /// <summary>Selects the ChannelRatio kernel operation.</summary>
        ChannelRatio = 13,
        /// <summary>Selects the DoubleExpected kernel operation.</summary>
        DoubleExpected = 15,
        /// <summary>Selects the SelectedExpected kernel operation.</summary>
        SelectedExpected = 16,
        /// <summary>Selects the Intervals kernel operation.</summary>
        Intervals = 17,
        /// <summary>Selects the AffineInterval kernel operation.</summary>
        AffineInterval = 19,
        /// <summary>Compares authored per-component float tolerances with original float subtraction.</summary>
        FloatingTolerance = 22,
        /// <summary>Compares exact IEEE bits without numeric equivalence.</summary>
        ExactBits = 23,
        /// <summary>Reconstructs exact normalized byte values without a projected image.</summary>
        NormalizedBytes = 24,
        /// <summary>Compares all selected components with one authored vector.</summary>
        VectorConstant = 25,
        /// <summary>Checks independently nominated scalar interval records.</summary>
        ScalarIntervals = 28,
        /// <summary>Checks every independently authored region/tuple, including overlaps.</summary>
        Regions = 29,
    }
    #endregion
}
