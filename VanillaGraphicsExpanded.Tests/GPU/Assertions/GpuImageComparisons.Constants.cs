using System.Numerics;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Rendering.RHI;

namespace VanillaGraphicsExpanded.Tests.GPU.Assertions;

/// <summary>Owns constant tuple comparisons without repeated pixel references.</summary>
internal sealed partial class GpuImageComparisons
{
    #region Internal API
    /// <summary>Compares a whole selected domain with one authored tuple, preserving its exact floating bits.</summary>
    internal GpuComparisonResult CompareVector(GpuTexture image, Vector4 expected,
        RHITextureRegion? selection = null, uint? channelMask = null, double tolerance = 0)
    {
        Programs.RequireContext();
        using var actual = new GpuImageSnapshot(image, selection);
        if (actual.Kind != 0) throw new ArgumentException("Vector expectations require floating or normalized storage.", nameof(image));
        var inputs = DomainInputs(actual, channelMask, tolerance, double.NegativeInfinity, double.PositiveInfinity);
        GpuAssertionInputs.Integer(inputs, 12, (uint)ComparisonOperation.VectorConstant);
        for (int c = 0; c < 4; c++) GpuAssertionInputs.Integer(inputs, 96 + c * 4, BitConverter.SingleToUInt32Bits(expected[c]));
        return new(CompareDomain(actual, "compare", inputs, [("actualImage", actual.Texture)],
            (pixel, channel, value) => expected[channel].ToString("R")));
    }

    /// <summary>Checks one integer constant over every represented selected component without a reference image.</summary>
    internal GpuConstantComparisonResult CompareUnsignedConstant(GpuTexture image, uint expected,
        RHITextureRegion? selection = null, uint? channelMask = null)
    {
        Programs.RequireContext();
        using var actual = new GpuImageSnapshot(image, selection);
        if (actual.Kind != 2) throw new ArgumentException("Unsigned constants require unsigned integer storage.", nameof(image));
        var inputs = DomainInputs(actual, channelMask, 0, 0, uint.MaxValue);
        GpuAssertionInputs.Integer(inputs, 12, (uint)ComparisonOperation.Constant); GpuAssertionInputs.Integer(inputs, 20, expected);
        return new(CompareDomain(actual, "compare", inputs, [("actualImage", actual.Texture)],
            (pixel, channel, value) => $"{expected}/0x{expected:x8}"));
    }

    /// <summary>Checks one exact integer tuple without a repeated per-pixel reference allocation.</summary>
    internal GpuComparisonResult CompareIntegerVector(GpuTexture image, uint x, uint y, uint z, uint w)
    {
        Programs.RequireContext();
        using var actual = new GpuImageSnapshot(image);
        if (actual.Kind == 0) throw new ArgumentException("Integer vectors require signed or unsigned integer storage.", nameof(image));
        uint[] expected = [x,y,z,w];
        var inputs = DomainInputs(actual, null, 0, double.NegativeInfinity, double.PositiveInfinity);
        GpuAssertionInputs.Integer(inputs, 12, (uint)ComparisonOperation.VectorConstant);
        for (int c = 0; c < 4; c++) GpuAssertionInputs.Integer(inputs, 96+c*4, expected[c]);
        return new(CompareDomain(actual, "compare", inputs, [("actualImage", actual.Texture)],
            (pixel, channel, value) => FormatValue(expected[channel],actual.Kind)));
    }

    #endregion
}
