using VanillaGraphicsExpanded.LumOn.WorldProbes.Tracing;
using VanillaGraphicsExpanded.Numerics;

using Xunit;

namespace VanillaGraphicsExpanded.Tests.Unit.LumOn.WorldProbes.Tracing;

public sealed class ProbeHitFaceUtilTests
{
    [Theory]
    [InlineData(0, 0, -1, ProbeHitFace.North)]
    [InlineData(1, 0, 0, ProbeHitFace.East)]
    [InlineData(0, 0, 1, ProbeHitFace.South)]
    [InlineData(-1, 0, 0, ProbeHitFace.West)]
    [InlineData(0, 1, 0, ProbeHitFace.Up)]
    [InlineData(0, -1, 0, ProbeHitFace.Down)]
    public void FromAxisNormal_MapsCanonicalAxesToFaces(int x, int y, int z, ProbeHitFace expected)
    {
        var n = new VectorInt3(x, y, z);
        ProbeHitFace face = ProbeHitFaceUtil.FromAxisNormal(n);
        Assert.Equal(expected, face);
    }
}
