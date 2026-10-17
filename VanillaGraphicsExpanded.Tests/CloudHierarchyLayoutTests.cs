using VanillaGraphicsExpanded.PBR.Clouds;

namespace VanillaGraphicsExpanded.Tests;

/// <summary>Checks the logical native-leaf grid separately from owned coarse texture mip dimensions.</summary>
public sealed class CloudHierarchyLayoutTests
{
    #region Public API
    /// <summary>All admitted widths preserve native size while coarse footprints cover the logical padded square.</summary>
    [Fact]
    public void AllSupportedWidthsHaveOnlyCoarseOwnedLevels()
    {
        for (int native = 20; native <= 200; native++)
        {
            var layout = new CloudHierarchyLayout(native);
            Assert.Equal(native, layout.NativeWidth);
            int padded = 1;
            while (padded < native) padded *= 2;
            Assert.Equal(padded, layout.LeafWidth);
            int payload = 0;
            for (int mip = 0; mip < layout.MipCount; mip++)
            {
                int width = padded / (1 << (mip + 1));
                Assert.Equal(width, layout.MipWidth(mip));
                Assert.Equal(padded, layout.CellsPerNode(mip) * width);
                Assert.Equal(50 * (1 << (mip + 1)), layout.CellsPerNode(mip) * CloudHierarchyLayout.CellSize);
                payload += width * width * 8;
            }
            Assert.Equal(1, layout.MipWidth(layout.MipCount - 1));
            Assert.Equal(payload, layout.PayloadBytes);
        }
    }

    /// <summary>The maximum native grid requires eight coarse mips and no 256-square owned leaf copy.</summary>
    [Fact]
    public void MaximumLayoutMatchesApprovedStorageBudget()
    {
        var layout = new CloudHierarchyLayout(200);
        Assert.Equal(256, layout.LeafWidth);
        Assert.Equal(128, layout.MipWidth(0));
        Assert.Equal(2, layout.CellsPerNode(0));
        Assert.Equal(8, layout.MipCount);
        Assert.Equal(21845 * 8, layout.PayloadBytes);
    }

    /// <summary>Unsupported widths and invalid owned mips fail before arithmetic can wrap.</summary>
    [Fact]
    public void InvalidDimensionsAndMipIndicesAreRejected()
    {
        foreach (int width in new[] { int.MinValue, -1, 0, 19, 201, int.MaxValue })
            Assert.Throws<ArgumentOutOfRangeException>(() => new CloudHierarchyLayout(width));
        var layout = new CloudHierarchyLayout(81);
        foreach (int mip in new[] { int.MinValue, -1, layout.MipCount, int.MaxValue })
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => layout.MipWidth(mip));
            Assert.Throws<ArgumentOutOfRangeException>(() => layout.CellsPerNode(mip));
        }
    }
    #endregion
}
