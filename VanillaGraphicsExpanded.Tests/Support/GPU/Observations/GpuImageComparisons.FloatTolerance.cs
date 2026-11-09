using System.Numerics;
using VanillaGraphicsExpanded.Rendering;

namespace VanillaGraphicsExpanded.Tests.Support.GPU.Observations;

/// <summary>Preserves authored float subtraction/tolerances while packing only selected expectation fields.</summary>
internal sealed partial class GpuImageComparisons
{
    #region Internal API
    /// <summary>Checks original component-addressed expectations with each component's original absolute float tolerance.</summary>
    internal GpuComparisonResult CompareFloating(GpuTexture image, float[] expected, float[] tolerances, uint? channelMask = null)
        => CompareFloatingCore(image, expected, tolerances, channelMask, false);

    /// <summary>Checks independent expectations containing only selected components, without unselected CPU lanes.</summary>
    internal GpuComparisonResult CompareComponentFloating(GpuTexture image, float[] expected, float[] tolerances, uint channelMask)
        => CompareFloatingCore(image, expected, tolerances, channelMask, true);
    #endregion

    #region Private
    /// <summary>Validates all authored inputs and interleaves only selected values/tolerances before synchronous comparison.</summary>
    private GpuComparisonResult CompareFloatingCore(GpuTexture image, float[] expected, float[] tolerances, uint? channelMask, bool compact)
    {
        Programs.RequireContext();
        using var actual = new GpuObservationImage(image);
        var inputs = DomainInputs(actual, channelMask, 0, double.NegativeInfinity, double.PositiveInfinity);
        uint mask = channelMask ?? (1u << actual.Channels) - 1;
        int selectedChannels = BitOperations.PopCount(mask), sourceChannels = compact ? selectedChannels : actual.Channels;
        int count = checked(actual.Texture.Width * actual.Texture.Height * actual.Texture.Depth);
        if (actual.Kind != 0 || expected.Length != checked(count * sourceChannels) || tolerances.Length != expected.Length)
            throw new ArgumentException("Float expectations and tolerances must cover their complete authored domain.");
        uint[] bits = new uint[checked(count * selectedChannels * 2)];
        int destination = 0;
        for (int scalar = 0; scalar < expected.Length; scalar++)
        {
            if (!float.IsFinite(expected[scalar]) || !float.IsFinite(tolerances[scalar]) || tolerances[scalar] < 0)
                throw new ArgumentException("Float expectations require finite values and nonnegative finite tolerances.");
            if (!compact && (mask & (1u << (scalar % actual.Channels))) == 0) continue;
            bits[destination++] = BitConverter.SingleToUInt32Bits(expected[scalar]);
            bits[destination++] = BitConverter.SingleToUInt32Bits(tolerances[scalar]);
        }
        using var input = CreateWords(bits);
        GpuAssertionInputs.Integer(inputs, 12, (uint)ComparisonOperation.FloatingTolerance);
        GpuAssertionInputs.Integer(inputs, 80, (uint)selectedChannels);
        return new(CompareDomain(actual, "compare", inputs, [("actualImage", actual.Texture), ("expectedImage", input)],
            (pixel, channel, value) =>
            {
                int scalar = pixel * sourceChannels + (compact ? BitOperations.PopCount(mask & ((1u << channel) - 1)) : channel);
                return $"{expected[scalar]:R} +/- {tolerances[scalar]:R}";
            }));
    }
    #endregion
}
