using System;

using Vintagestory.API.MathTools;

using VanillaGraphicsExpanded.LumOn.WorldProbes;

using Xunit;

namespace VanillaGraphicsExpanded.Tests.Unit.LumOn.WorldProbes;

public sealed class ClipmapTopologyTests
{
    [Theory]
    [InlineData(0.0, 1.0, 0.0)]
    [InlineData(0.999, 1.0, 0.0)]
    [InlineData(1.0, 1.0, 1.0)]
    [InlineData(-0.001, 1.0, -1.0)]
    [InlineData(-1.0, 1.0, -1.0)]
    [InlineData(3.75, 0.5, 3.5)]
    public void SnapAnchor_FloorsToGrid(double x, double spacing, double expected)
    {
        Vec3d cam = new(x, 0, 0);
        Vec3d anchor = LumOnClipmapTopology.SnapAnchor(cam, spacing);
        Assert.Equal(expected, anchor.X, 10);
    }

    [Fact]
    public void OriginMinCorner_CentersCameraNearMiddle_ForEvenResolution()
    {
        Vec3d cam = new(10.25, 0, 0);
        double spacing = 2.0;
        int resolution = 32;

        Vec3d anchor = LumOnClipmapTopology.SnapAnchor(cam, spacing);
        Vec3d origin = LumOnClipmapTopology.GetOriginMinCorner(anchor, spacing, resolution);

        Vec3d localCam = LumOnClipmapTopology.WorldToLocal(cam, origin, spacing);

        Assert.InRange(localCam.X, resolution / 2.0, resolution / 2.0 + 1.0);
    }

    [Fact]
    public void WorldToLocal_IndexFrac_ProbeCenterIsStable()
    {
        Vec3d cam = new(0, 0, 0);
        double baseSpacing = 2.0;
        int level = 1;
        double spacing = LumOnClipmapTopology.GetSpacing(baseSpacing, level);

        int resolution = 32;
        Vec3d anchor = LumOnClipmapTopology.SnapAnchor(cam, spacing);
        Vec3d origin = LumOnClipmapTopology.GetOriginMinCorner(anchor, spacing, resolution);

        Vec3i index = new(7, 8, 9);
        Vec3d probeCenter = LumOnClipmapTopology.IndexToProbeCenterWorld(index, origin, spacing);

        Vec3d local = LumOnClipmapTopology.WorldToLocal(probeCenter, origin, spacing);
        Vec3i index2 = LumOnClipmapTopology.LocalToIndexFloor(local);
        Vec3d frac2 = LumOnClipmapTopology.LocalToFrac(local, index2);

        Assert.Equal(index.X, index2.X);
        Assert.Equal(index.Y, index2.Y);
        Assert.Equal(index.Z, index2.Z);

        Assert.Equal(0.5, frac2.X, 12);
        Assert.Equal(0.5, frac2.Y, 12);
        Assert.Equal(0.5, frac2.Z, 12);
    }

    [Theory]
    [InlineData(-1, 32, 31)]
    [InlineData(0, 32, 0)]
    [InlineData(31, 32, 31)]
    [InlineData(32, 32, 0)]
    [InlineData(33, 32, 1)]
    public void WrapIndex_IsPositiveModulo(int value, int resolution, int expected)
    {
        Assert.Equal(expected, LumOnClipmapTopology.WrapIndex(value, resolution));
    }

    [Fact]
    public void DistanceToBoundary_DecreasesNearEdge()
    {
        int res = 32;
        Vec3d nearCenter = new(16, 16, 16);
        Vec3d nearEdge = new(0.1, 16, 16);

        double dCenter = LumOnClipmapTopology.DistanceToBoundaryProbeUnits(nearCenter, res);
        double dEdge = LumOnClipmapTopology.DistanceToBoundaryProbeUnits(nearEdge, res);

        Assert.True(dCenter > dEdge);
        Assert.True(dEdge < 1.0);
    }

    [Fact]
    public void ComputeCrossLevelBlendWeight_ClampsTo01()
    {
        Assert.Equal(0.0, LumOnClipmapTopology.ComputeCrossLevelBlendWeight(edgeDistProbeUnits: 0.0, blendStartProbeUnits: 2.0, blendWidthProbeUnits: 2.0));
        Assert.Equal(1.0, LumOnClipmapTopology.ComputeCrossLevelBlendWeight(edgeDistProbeUnits: 10.0, blendStartProbeUnits: 2.0, blendWidthProbeUnits: 2.0));
    }

    [Fact]
    public void SelectLevelByDistance_IsMonotonic()
    {
        Vec3d cam = new(0, 0, 0);
        double baseSpacing = 2.0;
        int maxLevel = 5;

        int l0 = LumOnClipmapTopology.SelectLevelByDistance(new Vec3d(1, 0, 0), cam, baseSpacing, maxLevel);
        int l1 = LumOnClipmapTopology.SelectLevelByDistance(new Vec3d(4, 0, 0), cam, baseSpacing, maxLevel);
        int l2 = LumOnClipmapTopology.SelectLevelByDistance(new Vec3d(16, 0, 0), cam, baseSpacing, maxLevel);

        Assert.True(l0 <= l1);
        Assert.True(l1 <= l2);
    }
}
