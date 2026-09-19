using Vintagestory.API.MathTools;

namespace VanillaGraphicsExpanded.Tests.Unit.LumOn.WorldProbes;

public sealed class WorldProbeCoordinateSpaceTests
{
    [Fact]
    public void PlayerRelativeCoordinates_ProduceWorldStableLookup()
    {
        Vec3d surfacePlayerRelative = new(1.25, 0.5, -1.25);
        Vec3d clipmapOriginWorld = new(96.0, 60.0, 96.0);
        Vec3d playerOriginWorld = new(100.0, 64.0, 100.0);

        Vec3d lookup = ComputeShaderLookupPosition(
            surfacePlayerRelative,
            clipmapOriginWorld,
            playerOriginWorld);

        Assert.Equal(5.25, lookup.X, 10);
        Assert.Equal(4.5, lookup.Y, 10);
        Assert.Equal(2.75, lookup.Z, 10);
    }

    [Fact]
    public void CameraBob_DoesNotAffectPlayerRelativeLookup()
    {
        Vec3d surfacePlayerRelative = new(1.25, 0.5, -1.25);
        Vec3d clipmapOriginWorld = new(96.0, 60.0, 96.0);
        Vec3d playerOriginWorld = new(100.0, 64.0, 100.0);

        Vec3d lookup = ComputeShaderLookupPosition(
            surfacePlayerRelative,
            clipmapOriginWorld,
            playerOriginWorld);

        Assert.Equal(5.25, lookup.X, 10);
        Assert.Equal(4.5, lookup.Y, 10);
        Assert.Equal(2.75, lookup.Z, 10);
    }

    private static Vec3d ComputeShaderLookupPosition(
        Vec3d surfacePlayerRelative,
        Vec3d clipmapOriginWorld,
        Vec3d playerOriginWorld)
    {
        Vec3d originPlayerRelative = clipmapOriginWorld - playerOriginWorld;
        return surfacePlayerRelative - originPlayerRelative;
    }
}