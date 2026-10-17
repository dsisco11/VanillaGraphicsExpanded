using System.Numerics;
using VanillaGraphicsExpanded.PBR.Clouds;

namespace VanillaGraphicsExpanded.Tests;

/// <summary>Checks native-leaf interval algebra and coarse summaries against independent descendant enumeration.</summary>
public sealed class CloudHierarchyBoundsTests
{
    #region Public API
    /// <summary>Every generated node encloses all native descendants, including odd edges and logical padding.</summary>
    [Theory]
    [InlineData(20)]
    [InlineData(21)]
    [InlineData(31)]
    [InlineData(32)]
    [InlineData(33)]
    [InlineData(81)]
    [InlineData(127)]
    [InlineData(128)]
    [InlineData(129)]
    [InlineData(163)]
    [InlineData(200)]
    public void CoarseReductionMatchesDirectDescendantEnumeration(int nativeWidth)
    {
        var layout = new CloudHierarchyLayout(nativeWidth);
        foreach (int pattern in new[] { 0, 1, 2 })
        {
            var columns = Columns(nativeWidth, pattern);
            var summaries = Reduce(columns, layout);
            for (int mip = 0; mip < summaries.Length; mip++)
            {
                int span = 1 << (mip + 1), width = layout.MipWidth(mip);
                for (int z = 0; z < width; z++)
                for (int x = 0; x < width; x++)
                {
                    var expected = CloudHierarchyBoundsOracle.Bounds(columns, nativeWidth, x * span, z * span, span);
                    var actual = summaries[mip][z * width + x];
                    Assert.Equal(expected.Lower, actual.Lower);
                    Assert.Equal(expected.Upper, actual.Upper);
                }
            }
        }
    }

    /// <summary>Every shader-rejected component is excluded, while finite green and red above one are retained.</summary>
    [Fact]
    public void ColumnAdmissionMatchesShaderValidityAndOccupancy()
    {
        foreach (float invalid in new[] { float.NaN, float.PositiveInfinity, float.NegativeInfinity })
        for (int component = 0; component < 4; component++)
        {
            Vector4 column = new(2, 0, -1, 3);
            column[component] = invalid;
            Assert.Equal(CloudHeightInterval.Empty, CloudHeightInterval.FromColumn(column));
        }
        foreach (var column in new[] { new Vector4(-1, 0, -1, 3), new Vector4(0, 0, -1, 3),
                     new Vector4(1, 0, 3, 3), new Vector4(1, 0, 4, 3) })
            Assert.True(CloudHeightInterval.FromColumn(column).IsEmpty);
        Assert.Equal(new(-50, 150), CloudHeightInterval.FromColumn(new(2, -7, -1, 3)));
        Assert.Equal(new(-50, 150), CloudHeightInterval.FromColumn(new(float.Epsilon, 0, -1, 3)));
    }

    /// <summary>Empty nodes are neutral and separated clouds leave a conservative gap inside their parent.</summary>
    [Fact]
    public void ReductionPreservesExtremaWithoutInventingOccupancyRules()
    {
        var low = CloudHeightInterval.FromColumn(new(1, 0, -4, -3));
        var high = CloudHeightInterval.FromColumn(new(1, 0, 2, 7));
        Assert.True(CloudHeightInterval.Empty.Enclose(CloudHeightInterval.Empty).IsEmpty);
        Assert.Equal(low, low.Enclose(CloudHeightInterval.Empty));
        Assert.Equal(high, CloudHeightInterval.Empty.Enclose(high));
        Assert.Equal(new(-200, 350), low.Enclose(high));
        Assert.Equal(low.Enclose(high), high.Enclose(low));
    }

    /// <summary>Finite admitted source endpoints that overflow world scaling remain conservatively unbounded.</summary>
    [Fact]
    public void WorldConversionDoesNotDiscardExtremeValidColumns()
    {
        var extreme = CloudHeightInterval.FromColumn(new(1, 0, -float.MaxValue, float.MaxValue));
        Assert.False(extreme.IsEmpty);
        Assert.Equal(float.NegativeInfinity, extreme.Lower);
        Assert.Equal(float.PositiveInfinity, extreme.Upper);
        Assert.Equal(extreme, extreme.Enclose(CloudHeightInterval.Empty));
    }
    #endregion

    #region Private
    /// <summary>Builds empty, sparse and dense fixtures with unequal heights and invalid controls at map edges.</summary>
    private static Vector4[] Columns(int width, int pattern)
    {
        var columns = new Vector4[width * width];
        if (pattern == 0) return columns;
        for (int z = 0; z < width; z++)
        for (int x = 0; x < width; x++)
        {
            int index = z * width + x;
            if (pattern == 1 && index % 19 != 0) continue;
            float lower = ((x * 13 + z * 7) % 29 - 15) * .137f;
            columns[index] = new(index % 11 == 0 ? float.Epsilon : 3, 0, lower,
                lower + .003f + (index % 23) * .041f);
        }
        // The far corner must survive first-level partial blocks; adjacent invalid cells cannot poison it.
        columns[^1] = new(1, 0, -10.25f, 40.125f);
        columns[^2] = new(1, float.NaN, -100, 100);
        columns[^3] = new(-1, 0, -100, 100);
        columns[^4] = new(1, 0, 5, -5);
        return columns;
    }

    /// <summary>Exercises the proposed pairwise reduction contract without producing a runtime CPU hierarchy.</summary>
    private static CloudHeightInterval[][] Reduce(Vector4[] columns, CloudHierarchyLayout layout)
    {
        var levels = new CloudHeightInterval[layout.MipCount][];
        for (int mip = 0; mip < levels.Length; mip++)
        {
            int width = layout.MipWidth(mip);
            levels[mip] = new CloudHeightInterval[width * width];
            for (int z = 0; z < width; z++)
            for (int x = 0; x < width; x++)
            {
                var summary = CloudHeightInterval.Empty;
                for (int dz = 0; dz < 2; dz++)
                for (int dx = 0; dx < 2; dx++)
                {
                    int childX = x * 2 + dx, childZ = z * 2 + dz;
                    CloudHeightInterval child;
                    if (mip == 0)
                        child = childX < layout.NativeWidth && childZ < layout.NativeWidth
                            ? CloudHeightInterval.FromColumn(columns[childZ * layout.NativeWidth + childX])
                            : CloudHeightInterval.Empty;
                    else child = levels[mip - 1][childZ * (width * 2) + childX];
                    summary = summary.Enclose(child);
                }
                levels[mip][z * width + x] = summary;
            }
        }
        return levels;
    }
    #endregion
}
