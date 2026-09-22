using System.Numerics;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.LumOn.Scene.Geometry;
using VanillaGraphicsExpanded.Numerics;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Tests.Fixtures.WorldProbes;
using VanillaGraphicsExpanded.Tests.GPU.Fixtures;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;

namespace VanillaGraphicsExpanded.Tests.GPU;

/// <summary>Reproduces stale material identities through production capture and GPU publication, independently of live scheduling.</summary>
[Collection("NearFieldMaterialCapture")]
[Trait("Category", "GPU")]
public sealed class LumOnNearFieldMaterialReadinessTests : NearFieldShaderTestBase
{
    #region Construction
    /// <summary>Uses an exclusive GPU context while temporarily changing the singleton registry.</summary>
    public LumOnNearFieldMaterialReadinessTests(HeadlessGLFixture fixture) : base(fixture) { }
    #endregion

    #region Material Lifecycle
    /// <summary>Ready registry data alone does not update previously published material tables; a new generation and recapture repair them.</summary>
    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    public void CapturedBeforeMaterialReadiness_StaysUnresolvedUntilRecaptured(bool surfaceReady, bool derivedReady)
    {
        EnsureShaderTestAvailable();
        using var material = new ScopedPbrMaterialFixture();
        material.SetReadiness(surfaceReady, derivedReady);
        var world = CreateRoom(material.Cube);
        uint captured = TraceGeometryVoxel.Classify(ControlledBlockAccessor.Create(world), material.Cube,
            new BlockPos(-3, 0, -5));
        Assert.Equal(2u, captured);
        var worldProbe = WorldProbeRoomScenario.Trace(world, new Vector3d(0, 0, -5));
        Assert.True(worldProbe.Success);
        Assert.All(worldProbe.AtlasSamples, sample => Assert.True(sample.RadianceRgb.Length() > 0.1f));

        using var fixture = new NearFieldVoxelFixture();
        fixture.PublishCaptured(world);
        AssertPublished(fixture.Scene);
        AssertLighting(Trace(fixture), lit: false, opaque: true);
        long revision = fixture.Scene.Revision;
        material.SetReadiness(true, true);
        fixture.Pump();
        Assert.Equal(revision, fixture.Scene.Revision);
        AssertPublished(fixture.Scene);
        AssertLighting(Trace(fixture), lit: false, opaque: true);

        // Keep geometry and chunk versions unchanged: a new material-table generation and cell capture resolve the hit-lighting data.
        fixture.PublishCaptured(world);
        Assert.True(fixture.Scene.Revision > revision);
        AssertPublished(fixture.Scene);
        AssertLighting(Trace(fixture), lit: true, opaque: true);
    }

    /// <summary>A fully ready registry lights the room on its first capture; a real partial-block capture remains unsupported.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ReadyMaterial_CaptureRespectsSupportedGeometry(bool partial)
    {
        EnsureShaderTestAvailable();
        using var material = new ScopedPbrMaterialFixture();
        material.SetReadiness(true, true);
        if (partial) material.Cube.CollisionBoxes = [new Cuboidf(0, 0, 0, 1, 0.5f, 1)];
        var world = CreateRoom(material.Cube);
        using var fixture = new NearFieldVoxelFixture();
        fixture.PublishCaptured(world);
        AssertPublished(fixture.Scene);
        AssertLighting(Trace(fixture), lit: !partial, opaque: !partial);
    }
    #endregion

    #region Controlled Scene And Assertions
    /// <summary>Builds a closed room from the actual textured block with illuminated interior air.</summary>
    private static ControlledVoxelWorld CreateRoom(Block block)
    {
        var world = new ControlledVoxelWorld();
        for (int z = -8; z <= -2; z++)
        for (int y = -3; y <= 3; y++)
        for (int x = -3; x <= 3; x++)
            if (x is -3 or 3 || y is -3 or 3 || z is -8 or -2) world.SetBlock(x, y, z, block);
        world.FillLight((-2, -2, -7), (2, 2, -3), new Vector4(0.25f, 0.25f, 0.25f, 0));
        return world;
    }

    /// <summary>Checks actual trace lighting, hit classification, confidence, and the green versus magenta diagnostic code.</summary>
    private static void AssertLighting((float[] Radiance, float[] Meta) result, bool lit, bool opaque)
    {
        for (int pixel = 0; pixel < result.Meta.Length / 2; pixel++)
        {
            uint flags = Flags(result.Meta[pixel * 2 + 1]);
            Assert.Equal(opaque, (flags & 1u) != 0);
            Assert.Equal(0u, flags & (1u << 5));
            Assert.Equal(lit ? 2u : 4u, (flags >> 16) & 7u);
            Assert.Equal(lit ? 1f : 0f, result.Meta[pixel * 2]);
            for (int channel = 0; channel < 3; channel++)
                Assert.InRange(result.Radiance[pixel * 4 + channel], lit ? 0.249f : 0f, lit ? 0.253f : 0f);
        }
    }

    /// <summary>Proves all captured regions remain GPU-ready even when their material identities are unresolved.</summary>
    private static void AssertPublished(ControlledTraceGpuScene scene)
    {
        var readiness = new byte[scene.RegionResolution * scene.RegionResolution * scene.RegionResolution];
        GL.GetInteger(GetPName.PackAlignment, out int previous);
        using var binding = GlStateCache.Current.BindTextureScope(TextureTarget.Texture3D, 0, scene.Regions.TextureId);
        try
        {
            GL.PixelStore(PixelStoreParameter.PackAlignment, 1);
            GL.GetTexImage(TextureTarget.Texture3D, 0, PixelFormat.RedInteger, PixelType.UnsignedByte, readiness);
            Assert.All(readiness, value => Assert.Equal(1, value));
        }
        finally { GL.PixelStore(PixelStoreParameter.PackAlignment, previous); }
    }
    #endregion
}
