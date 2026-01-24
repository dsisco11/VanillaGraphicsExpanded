using VanillaGraphicsExpanded.LumOn.WorldProbes.Tracing;
using VanillaGraphicsExpanded.Numerics;

using Xunit;

namespace VanillaGraphicsExpanded.Tests.Unit.LumOn.WorldProbes.Tracing;

public sealed class ProbeHitFaceUtilTests
{
    [Theory]
    [InlineData(0, 0, -1, 0)]
    [InlineData(1, 0, 0, 1)]
    [InlineData(0, 0, 1, 2)]
    [InlineData(-1, 0, 0, 3)]
    [InlineData(0, 1, 0, 4)]
    [InlineData(0, -1, 0, 5)]
    public void FromAxisNormal_MapsCanonicalAxesToFaces(int x, int y, int z, byte expectedFaceIndex)
    {
        var n = new VectorInt3(x, y, z);
        ProbeHitFace face = ProbeHitFaceUtil.FromAxisNormal(n);
        Assert.Equal(expectedFaceIndex, (byte)face);
    }
}
