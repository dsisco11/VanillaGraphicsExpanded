using System.Numerics;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Rendering.RHI;

namespace VanillaGraphicsExpanded.Tests.Support.GPU.Observations;

/// <summary>Owns independent references packed by the authored component mask.</summary>
internal sealed partial class GpuImageComparisons
{
    #region Internal API
    /// <summary>Compares independent CPU doubles containing only the selected channels, retaining their exact original precision.</summary>
    internal GpuComparisonResult CompareComponentDoubles(GpuTexture image, double[] expected, double tolerance,
        uint channelMask, RHITextureRegion? selection = null)
    {
        Programs.RequireContext();
        using var actual = new GpuObservationImage(image, selection);
        var inputs = DomainInputs(actual, channelMask, tolerance, double.NegativeInfinity, double.PositiveInfinity);
        int channels = BitOperations.PopCount(channelMask);
        if (expected.Length != checked(actual.Texture.Width * actual.Texture.Height * actual.Texture.Depth * channels) ||
            expected.Any(value => !double.IsFinite(value)))
            throw new ArgumentException("Finite double expectations must cover all selected components.", nameof(expected));
        using var input = CreateDoubleInput(channels, [expected], null);
        GpuAssertionInputs.Integer(inputs, 12, (uint)ComparisonOperation.DoubleExpected);
        GpuAssertionInputs.Integer(inputs, 80, (uint)channels);
        return new(CompareDomain(actual, "compare", inputs, [("actualImage", actual.Texture), ("expectedImage", input)],
            (pixel, channel, value) => expected[pixel * channels + BitOperations.PopCount(channelMask & ((1u << channel) - 1))].ToString("R")));
    }

    /// <summary>Compares independent floats containing only selected components, retaining numeric IEEE equality policy.</summary>
    internal GpuComparisonResult CompareComponents(GpuTexture image, float[] expected, uint channelMask, double tolerance = 0)
    {
        Programs.RequireContext();
        using var actual = new GpuObservationImage(image);
        var inputs = DomainInputs(actual, channelMask, tolerance, double.NegativeInfinity, double.PositiveInfinity);
        int channels = BitOperations.PopCount(channelMask);
        if (actual.Kind != 0 || expected.Length != checked(actual.Texture.Width * actual.Texture.Height * actual.Texture.Depth * channels))
            throw new ArgumentException("Float expectations must cover every selected component.", nameof(expected));
        using var input = CreateWords(Array.ConvertAll(expected, BitConverter.SingleToUInt32Bits));
        GpuAssertionInputs.Integer(inputs, 12, (uint)ComparisonOperation.ExpectedBits);
        GpuAssertionInputs.Integer(inputs, 80, (uint)channels);
        return new(CompareDomain(actual, "compare", inputs, [("actualImage", actual.Texture), ("expectedImage", input)],
            (pixel, channel, value) => expected[pixel * channels + BitOperations.PopCount(channelMask & ((1u << channel) - 1))].ToString("R")));
    }

    /// <summary>Checks compact independent bounds containing only the channels selected by the authored mask.</summary>
    internal GpuComparisonResult CompareComponentIntervals(GpuTexture image, double[] lower, double[] upper,
        uint channelMask, RHITextureRegion? selection = null)
    {
        Programs.RequireContext();
        using var actual = new GpuObservationImage(image, selection);
        var inputs = DomainInputs(actual, channelMask, 0, double.NegativeInfinity, double.PositiveInfinity);
        int channels = BitOperations.PopCount(channelMask);
        int count = checked(actual.Texture.Width * actual.Texture.Height * actual.Texture.Depth * channels);
        if (lower.Length != count || upper.Length != count) throw new ArgumentException("Bounds must cover every selected component.");
        for (int i = 0; i < count; i++)
            if (double.IsNaN(lower[i]) || double.IsNaN(upper[i]) || lower[i] > upper[i])
                throw new ArgumentException("Bounds must be ordered and contain no NaN.");
        using var input = CreateDoubleInput(channels, [lower, upper], null);
        GpuAssertionInputs.Integer(inputs, 12, (uint)ComparisonOperation.Intervals);
        GpuAssertionInputs.Integer(inputs, 80, (uint)channels);
        return new(CompareDomain(actual, "compare", inputs, [("actualImage", actual.Texture), ("expectedImage", input)],
            (pixel, channel, value) =>
            {
                int scalar = pixel * channels + BitOperations.PopCount(channelMask & ((1u << channel) - 1));
                return $"[{lower[scalar]:R},{upper[scalar]:R}]";
            }));
    }

    #endregion
}
