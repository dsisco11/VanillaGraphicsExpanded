using VanillaGraphicsExpanded.LumOn.WorldProbes.Tracing;

using Xunit;

namespace VanillaGraphicsExpanded.Tests.Unit.LumOn.WorldProbes;

public sealed class WorldProbeTraceDirectionsTests
{
    [Fact]
    public void Directions_CountMatchesOctahedralTile()
    {
        var dirs = LumOnWorldProbeTraceDirections.GetDirections();
        Assert.Equal(LumOnWorldProbeTraceDirections.OctahedralSize * LumOnWorldProbeTraceDirections.OctahedralSize, dirs.Length);
    }

    [Fact]
    public void Directions_AreNormalized()
    {
        var dirs = LumOnWorldProbeTraceDirections.GetDirections();
        foreach (var d in dirs)
        {
            float len2 = d.X * d.X + d.Y * d.Y + d.Z * d.Z;
            Assert.InRange(len2, 0.999f, 1.001f);
        }
    }
}
