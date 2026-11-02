using System.Numerics;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Rendering.RHI;

namespace VanillaGraphicsExpanded.Tests.GPU.Assertions;

/// <summary>Owns nominated comparison records and original source-coordinate failure evidence.</summary>
internal sealed partial class GpuImageComparisons
{
    #region Internal API
    /// <summary>Checks exactly the nominated RGB samples; completion and work scale with the selected records.</summary>
    internal GpuSparseComparisonResult CompareSelected(GpuTexture image, IReadOnlyDictionary<int, Vector3> expected,
        double tolerance)
    {
        Programs.RequireContext();
        if (expected.Count == 0) throw new ArgumentException("Select a nonempty set of RGB samples.", nameof(expected));
        using var actual = new GpuImageSnapshot(image);
        if (actual.Kind != 0 || actual.Channels < 3) throw new ArgumentException("Selected samples require floating RGB.", nameof(image));
        int count = checked(image.Width * image.Height * image.Depth);
        uint[] words = new uint[checked(expected.Count * 4)];
        int offset = 0;
        foreach (var (pixel, value) in expected)
        {
            if ((uint)pixel >= (uint)count) throw new ArgumentOutOfRangeException(nameof(expected));
            words[offset++] = (uint)pixel;
            words[offset++] = BitConverter.SingleToUInt32Bits(value.X);
            words[offset++] = BitConverter.SingleToUInt32Bits(value.Y);
            words[offset++] = BitConverter.SingleToUInt32Bits(value.Z);
        }
        return new(CompareRecords(actual, words, expected.Count, ComparisonOperation.SelectedExpected, tolerance,
            (pixel, channel, value) => expected[pixel].ToString()));
    }

    /// <summary>Retains every independently authored interval, including duplicate/intersecting expectations at one component.</summary>
    internal GpuSparseComparisonResult CompareIntervals(GpuTexture image, IReadOnlyList<GpuScalarInterval> expected)
    {
        Programs.RequireContext();
        if (expected.Count == 0) throw new ArgumentException("Select a nonempty interval domain.", nameof(expected));
        using var actual = new GpuImageSnapshot(image);
        int count = checked(image.Width * image.Height * image.Depth);
        uint[] words = new uint[checked(expected.Count * 6)];
        for (int i = 0; i < expected.Count; i++)
        {
            var value = expected[i];
            if ((uint)value.Pixel >= (uint)count || (uint)value.Channel >= (uint)actual.Channels ||
                double.IsNaN(value.Lower) || double.IsNaN(value.Upper) || value.Lower > value.Upper)
                throw new ArgumentException("Intervals require represented coordinates and ordered non-NaN bounds.", nameof(expected));
            words[i * 6] = (uint)value.Pixel;
            words[i * 6 + 1] = (uint)value.Channel;
            ulong lower = BitConverter.DoubleToUInt64Bits(value.Lower), upper = BitConverter.DoubleToUInt64Bits(value.Upper);
            words[i * 6 + 2] = (uint)lower; words[i * 6 + 3] = (uint)(lower >> 32);
            words[i * 6 + 4] = (uint)upper; words[i * 6 + 5] = (uint)(upper >> 32);
        }
        return new(CompareRecords(actual, words, expected.Count, ComparisonOperation.ScalarIntervals, 0,
            (pixel, channel, value) => string.Join(" & ", expected.Where(item => item.Pixel == pixel && item.Channel == channel)
                .Select(item => $"[{item.Lower:R},{item.Upper:R}]"))));
    }

    #endregion

    #region Private
    /// <summary>Executes sparse records and reports failures at original source coordinates, without dense expansion.</summary>
    private GpuObservationData CompareRecords(GpuImageSnapshot actual, uint[] words, int records, ComparisonOperation mode,
        double tolerance, Func<int, int, uint[], string> expectedAt)
    {
        uint compared = checked((uint)records * (mode == ComparisonOperation.SelectedExpected ? 3u : 1u));
        GpuObservationLimits.Groups(records);
        using var input = CreateWords(words);
        var inputs = DomainInputs(actual, mode == ComparisonOperation.SelectedExpected ? 7u : 1u, tolerance, double.NegativeInfinity, double.PositiveInfinity);
        GpuAssertionInputs.Integer(inputs, 12, (uint)mode);
        GpuAssertionInputs.Integer(inputs, 20, (uint)records);
        var data = Programs.Dispatch.Observe(actual.Texture.Width, actual.Texture.Height, actual.Texture.Depth,
            actual.Kind, records, Programs.Get("compare", actual.Kind), inputs,
            [("actualImage", actual.Texture), ("expectedImage", input)]);
        if (data.Counters[19] != compared) throw new InvalidOperationException("Sparse comparison did not examine every nominated component.");
        return Diagnose(actual, data, expectedAt);
    }

    #endregion
}
