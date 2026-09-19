using Vintagestory.API.MathTools;

namespace VanillaGraphicsExpanded.Tests.Unit.LumOn.WorldProbes;

public sealed class WorldProbeCoordinateSpaceTests
{
    [Fact]
    public void MatchingCameraReferences_ProducesWorldStableLookup()
    {
        Vec3d surfaceWorld = new(101.25, 64.5, 98.75);
        Vec3d clipmapOriginWorld = new(96.0, 60.0, 96.0);
        Vec3d cameraReference = new(100.0, 65.62, 100.0);

        Vec3d lookup = ComputeShaderLookupPosition(
            surfaceWorld,
            clipmapOriginWorld,
            schedulerCameraReference: cameraReference,
            shaderCameraReference: cameraReference);

        Assert.Equal(surfaceWorld.X - clipmapOriginWorld.X, lookup.X, 10);
        Assert.Equal(surfaceWorld.Y - clipmapOriginWorld.Y, lookup.Y, 10);
        Assert.Equal(surfaceWorld.Z - clipmapOriginWorld.Z, lookup.Z, 10);
    }

    [Fact]
    public void BobbedRenderCamera_ShiftsLookupByCameraDelta()
    {
        Vec3d surfaceWorld = new(101.25, 64.5, 98.75);
        Vec3d clipmapOriginWorld = new(96.0, 60.0, 96.0);
        Vec3d schedulerCameraReference = new(100.0, 65.62, 100.0);
        Vec3d shaderCameraReference = new(100.0, 65.74, 100.0);

        Vec3d lookup = ComputeShaderLookupPosition(
            surfaceWorld,
            clipmapOriginWorld,
            schedulerCameraReference,
            shaderCameraReference);

        Assert.Equal(surfaceWorld.X - clipmapOriginWorld.X, lookup.X, 10);
        Assert.Equal(surfaceWorld.Y - clipmapOriginWorld.Y - 0.12, lookup.Y, 10);
        Assert.Equal(surfaceWorld.Z - clipmapOriginWorld.Z, lookup.Z, 10);
    }

    private static Vec3d ComputeShaderLookupPosition(
        Vec3d surfaceWorld,
        Vec3d clipmapOriginWorld,
        Vec3d schedulerCameraReference,
        Vec3d shaderCameraReference)
    {
        Vec3d originCameraRelative = clipmapOriginWorld - schedulerCameraReference;
        Vec3d surfaceCameraRelative = surfaceWorld - shaderCameraReference;
        return surfaceCameraRelative - originCameraRelative;
    }
}