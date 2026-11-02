using System.Numerics;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Rendering.RHI;

namespace VanillaGraphicsExpanded.Tests.GPU.Assertions;

/// <summary>Owns independent overlapping region tables without dense pixel expansion.</summary>
internal sealed partial class GpuImageComparisons
{
    #region Internal API
    /// <summary>Checks small authored region tables without expanding their tuples to every pixel; overlaps retain every expectation.</summary>
    internal GpuComparisonResult CompareRegions(GpuTexture image, IReadOnlyList<GpuVectorRegion> expected, double tolerance = 0)
    {
        Programs.RequireContext();
        using var actual = new GpuObservationImage(image);
        if (actual.Kind != 0 || expected.Count == 0) throw new ArgumentException("Regions require floating storage and nonempty authored coverage.");
        uint[] words = new uint[checked(expected.Count * 12)];
        uint compared = 0;
        for (int i = 0; i < expected.Count; i++)
        {
            var item = expected[i]; var r = item.Region;
            if (r.Subresource.MipLevel != 0 || r.Subresource.Layer != 0 || r.X < 0 || r.Y < 0 || r.Z < 0 ||
                r.Width <= 0 || r.Height <= 0 || r.Depth <= 0 || (long)r.X+r.Width > image.Width ||
                (long)r.Y+r.Height > image.Height || (long)r.Z+r.Depth > image.Depth || item.ChannelMask == 0 ||
                (item.ChannelMask & ~((1u << actual.Channels)-1)) != 0)
                throw new ArgumentException("Regions must select represented source coordinates and channels.", nameof(expected));
            int offset = i * 12;
            words[offset] = (uint)r.X; words[offset+1] = (uint)r.Y; words[offset+2] = (uint)r.Z;
            words[offset+3] = (uint)(r.X+r.Width); words[offset+4] = (uint)(r.Y+r.Height); words[offset+5] = (uint)(r.Z+r.Depth);
            words[offset+6] = item.ChannelMask;
            for (int c = 0; c < 4; c++) words[offset+8+c] = BitConverter.SingleToUInt32Bits(item.Expected[c]);
            compared = checked(compared + checked((uint)(r.Width*r.Height*r.Depth) * (uint)BitOperations.PopCount(item.ChannelMask)));
        }
        using var input = CreateWords(words);
        var inputs = DomainInputs(actual, null, tolerance, double.NegativeInfinity, double.PositiveInfinity);
        GpuAssertionInputs.Integer(inputs, 12, (uint)ComparisonOperation.Regions);
        GpuAssertionInputs.Integer(inputs, 20, (uint)expected.Count);
        var data = CompareDomain(actual, "compare", inputs, [("actualImage", actual.Texture), ("expectedImage", input)],
            (pixel, channel, value) => string.Join(" & ", expected.Where(item =>
            {
                int x=pixel%image.Width,y=pixel/image.Width%image.Height,z=pixel/(image.Width*image.Height);
                var r=item.Region;
                return (item.ChannelMask&(1u<<channel))!=0 && x>=r.X && x<r.X+r.Width && y>=r.Y && y<r.Y+r.Height && z>=r.Z && z<r.Z+r.Depth;
            }).Select(item=>item.Expected[channel].ToString("R"))));
        if (data.Counters[19] != compared) throw new InvalidOperationException("Region comparison did not examine every authored component.");
        return new(data);
    }

    #endregion
}
