using VanillaGraphicsExpanded.Tests.GPU.Assertions;
using VanillaGraphicsExpanded.Tests.GPU.Helpers;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.LumOn.Scene;
using VanillaGraphicsExpanded.Tests.GPU.Fixtures;

namespace VanillaGraphicsExpanded.Tests.GPU;

/// <summary>Checks dynamic surface illumination using real shared publication, invalidation and bounded GPU relighting.</summary>
[Collection("NearFieldMaterialCapture")]
[Trait("Category", "GpuIntegration")]
public sealed class SurfaceCacheDynamicLightingTests : RenderTestBase
{


    /// <summary>Uses the material-isolated GPU context.</summary>
    public SurfaceCacheDynamicLightingTests(HeadlessGLFixture fixture) : base(fixture) { }

    #region Lighting history transitions
    /// <summary>Light-only source changes retain initialized history until a separately scheduled refresh.</summary>
    [Fact]
    public void LightRemovalAndRestorationRetainStaleHistory()
    {
        EnsureContextValid();
        using var fixture = new DynamicSurfaceLightingFixture(initialLight: 32);
        var scene = fixture.Geometry.Scene;
        Assert.True(fixture.History.TrySynchronize(scene, fixture.Page.IrradianceAtlas, out bool initialized));
        Assert.True(initialized);
        Assert.True(fixture.Page.Capture(scene));
        for (int update = 0; update < 4; update++) Assert.True(fixture.Page.Relight(scene));
        AssertLighting(fixture.Page.IrradianceAtlas, 32*GgxHemisphereReference.CapturedDiffuse(1), 4);

        // Source publication and history synchronization are deliberately separate: invalid
        // geometry cannot manufacture successful dark samples while replacement data is pending.
        foreach (int light in new[] { 0, 32 })
        {
            long invalidation = scene.InvalidationRevision;
            long history = fixture.History.Revision;
            uint packed = LumonSceneOccupancyPacking.PackClamped(light, 0, 0, (int)fixture.MaterialId);
            fixture.Geometry.Sample = (_, _, _) => new(2u | fixture.MaterialId << 2, packed, 0);
            fixture.Geometry.Dirty();
            Assert.True(scene.InvalidationRevision > invalidation);
            Assert.True(fixture.History.TrySynchronize(scene, fixture.Page.IrradianceAtlas, out bool invalidated));
            Assert.False(invalidated);
            Assert.Equal(history, fixture.History.Revision);
            AssertLighting(fixture.Page.IrradianceAtlas, 32*GgxHemisphereReference.CapturedDiffuse(1), 4);
            Assert.False(fixture.Page.Relight(scene));
            AssertLighting(fixture.Page.IrradianceAtlas, 32*GgxHemisphereReference.CapturedDiffuse(1), 4);

            fixture.Geometry.Publish();
            Assert.True(fixture.History.TrySynchronize(scene, fixture.Page.IrradianceAtlas, out _));
            AssertLighting(fixture.Page.IrradianceAtlas, 32*GgxHemisphereReference.CapturedDiffuse(1), 4);
            Assert.Equal(history, fixture.History.Revision);
        }
        Assert.Equal(ErrorCode.NoError, GL.GetError());
    }

    /// <summary>Remote geometry edits retain valid captured lighting while its illumination becomes stale.</summary>
    [Fact]
    public void DoorwayClosureAndReopeningRetainStaleIllumination()
    {
        EnsureContextValid();
        var doorway = new ControlledSurfaceDoorwayScene();
        using var fixture = new DynamicSurfaceLightingFixture(doorway.Sample);
        var scene = fixture.Geometry.Scene;
        Assert.Equal(1u, fixture.Geometry.ReadGeometry(4, 33, 1) & 3u);
        uint capturedWall = fixture.Geometry.ReadGeometry(0, 33, 1);
        uint exteriorLight = doorway.Sample(fixture.MaterialId, 7, 33, 1).LegacyLight;
        Assert.Equal(32u, LumonSceneOccupancyPacking.UnpackBlockLevel(exteriorLight));
        Assert.True(fixture.History.TrySynchronize(scene, fixture.Page.IrradianceAtlas, out bool initial));
        Assert.True(initial);
        Assert.True(fixture.Page.Capture(scene));
        for (int update = 0; update < 4; update++) Assert.True(fixture.Page.Relight(scene, steps: 256));
        using var open = new GpuImageSnapshot(fixture.Page.IrradianceAtlas);
        var observation = ImageAssertions.Predicates.Predicate(open.Texture, SurfacePredicates.SurfaceCacheDynamicLightingTests_DoorwayClosureAndReopeningRetainStaleIllumination);
        observation.AssertEqual(); Assert.True(observation.MatchingTexels>0);

        foreach (bool isOpen in new[] { false, true })
        {
            long before = scene.InvalidationRevision;
            doorway.DoorOpen = isOpen;
            // Light data never changes: this transition affects voxel occupancy alone.
            Assert.Equal(exteriorLight, doorway.Sample(fixture.MaterialId, 7, 33, 1).LegacyLight);
            Assert.Equal(0u, LumonSceneOccupancyPacking.UnpackBlockLevel(doorway.Sample(fixture.MaterialId, 3, 33, 1).LegacyLight));
            fixture.Geometry.Dirty();
            Assert.True(scene.InvalidationRevision > before);
            Assert.True(fixture.History.TrySynchronize(scene, fixture.Page.IrradianceAtlas, out bool invalidated));
            Assert.False(invalidated);
            ImageAssertions.Comparisons.CompareHistory(fixture.Page.IrradianceAtlas,open).AssertEqual();
            Assert.False(fixture.Page.Relight(scene));
            ImageAssertions.Comparisons.CompareHistory(fixture.Page.IrradianceAtlas,open).AssertEqual();
            fixture.Geometry.Publish();
            Assert.Equal(isOpen ? 1u : 2u, fixture.Geometry.ReadGeometry(4, 33, 1) & 3u);
            Assert.Equal(capturedWall, fixture.Geometry.ReadGeometry(0, 33, 1));
            Assert.True(fixture.History.TrySynchronize(scene, fixture.Page.IrradianceAtlas, out _));
            ImageAssertions.Comparisons.CompareHistory(fixture.Page.IrradianceAtlas,open).AssertEqual();
        }
        Assert.Equal(ErrorCode.NoError, GL.GetError());
    }
    /// <summary>Checks every color channel and exact sample weight, distinguishing valid darkness from unresolved rays.</summary>
    private void AssertLighting(VanillaGraphicsExpanded.Rendering.GpuTexture image, float radiance, float weight)
    {
        Assert.Equal(8,image.Width);Assert.Equal(8,image.Height);
        ImageAssertions.Predicates.Predicate(image, SurfacePredicates.SurfaceCacheDynamicLightingTests_AssertLighting,
            parameters:new(radiance-.01f,radiance+.01f,weight,0)).AssertEqual();
    }
    #endregion
    #region Private
    /// <summary>Prepares this suite's declared observation programs before subject execution.</summary>
    private protected override void PrepareObservations(VanillaGraphicsExpanded.Tests.GPU.Assertions.GpuObservationPrograms programs)
    {
        base.PrepareObservations(programs);
        programs.Prepare("compare", 0, 0);
        programs.Prepare("predicate-surface", 0, 0);
    }
    #endregion
}