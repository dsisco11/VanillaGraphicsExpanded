using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.LumOn.Scene;
using VanillaGraphicsExpanded.Tests.GPU.Fixtures;

namespace VanillaGraphicsExpanded.Tests.GPU;

/// <summary>Checks dynamic surface illumination using real shared publication, invalidation and bounded GPU relighting.</summary>
[Collection("NearFieldMaterialCapture")]
[Trait("Category", "GPU")]
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
        AssertLighting(fixture.Page.ReadLighting(), 32, 4);

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
            AssertLighting(fixture.Page.ReadLighting(), 32, 4);
            Assert.False(fixture.Page.Relight(scene));
            AssertLighting(fixture.Page.ReadLighting(), 32, 4);

            fixture.Geometry.Publish();
            Assert.True(fixture.History.TrySynchronize(scene, fixture.Page.IrradianceAtlas, out _));
            AssertLighting(fixture.Page.ReadLighting(), 32, 4);
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
        float[] open = fixture.Page.ReadLighting();
        // Resolved bright directions retain their source radiance regardless of travel distance.
        Assert.Contains(open.Where((_, index) => index % 4 == 0), value => value > .5f);
        for (int i = 3; i < open.Length; i += 4) Assert.Equal(4f, open[i]);

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
            Assert.Equal(open, fixture.Page.ReadLighting());
            Assert.False(fixture.Page.Relight(scene));
            Assert.Equal(open, fixture.Page.ReadLighting());
            fixture.Geometry.Publish();
            Assert.Equal(isOpen ? 1u : 2u, fixture.Geometry.ReadGeometry(4, 33, 1) & 3u);
            Assert.Equal(capturedWall, fixture.Geometry.ReadGeometry(0, 33, 1));
            Assert.True(fixture.History.TrySynchronize(scene, fixture.Page.IrradianceAtlas, out _));
            Assert.Equal(open, fixture.Page.ReadLighting());
        }
        Assert.Equal(ErrorCode.NoError, GL.GetError());
    }
    /// <summary>Checks every color channel and exact sample weight, distinguishing valid darkness from unresolved rays.</summary>
    private static void AssertLighting(float[] pixels, float radiance, float weight)
    {
        Assert.Equal(8 * 8 * 4, pixels.Length);
        for (int i = 0; i < pixels.Length; i += 4)
        {
            for (int channel = 0; channel < 3; channel++) Assert.InRange(pixels[i + channel], radiance - .01f, radiance + .01f);
            Assert.Equal(weight, pixels[i + 3]);
        }
    }
    #endregion
}
