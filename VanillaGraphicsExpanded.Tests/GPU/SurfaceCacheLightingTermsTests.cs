using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.LumOn.Scene;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Tests.GPU.Fixtures;

namespace VanillaGraphicsExpanded.Tests.GPU;

/// <summary>Checks irradiance normalization independently of ray travel distance.</summary>
[Collection("NearFieldMaterialCapture")]
[Trait("Category", "GPU")]
public sealed class SurfaceCacheLightingTermsTests : RenderTestBase
{
    /// <summary>Uses the material-isolated graphics context.</summary>
    public SurfaceCacheLightingTermsTests(HeadlessGLFixture fixture) : base(fixture) { }

    #region Irradiance estimation
    /// <summary>A uniform enclosure yields irradiance scaled by hit reflectance, independent of wall distance.</summary>
    [Theory]
    [InlineData(5, 1f)]
    [InlineData(10, 1f)]
    [InlineData(5, .5f)]
    public void UniformEnclosurePreservesIrradianceAcrossTravelDistances(int extent, float reflectance)
    {
        EnsureContextValid();
        using var fixture = new DynamicSurfaceLightingFixture((id, x, y, z) =>
        {
            // The +X receiving patch is fixed; moving the other walls changes ray lengths only.
            bool wall = x <= 0 || x >= extent || y <= 32 - extent || y >= 36 + extent || z <= -extent || z >= 4 + extent;
            uint geometry = wall ? 2u | id << 2 : 1u;
            // Deliberately omit outside-cell material IDs: reflectance must come from the hit.
            uint light = LumonSceneOccupancyPacking.PackClamped(32, 0, 0, 0);
            return new(geometry, light, 0);
        }, new System.Numerics.Vector3(reflectance));
        Assert.True(fixture.Page.Capture(fixture.Geometry.Scene));
        Assert.True(fixture.Page.Relight(fixture.Geometry.Scene));
        float[] pixels = fixture.Page.ReadLighting();
        for (int i = 0; i < pixels.Length; i += 4)
        {
            // L = 32*reflectance/pi at each wall; E = pi*L for cosine-weighted sampling.
            float expected = 32f * MathF.Round(reflectance * 255f) / 255f;
            for (int channel = 0; channel < 3; channel++) Assert.InRange(pixels[i + channel], expected - .1f, expected + .1f);
            Assert.Equal(1f, pixels[i + 3]);
        }
    }

    /// <summary>Resolved directions cannot contribute a biased average when another direction exhausts traversal.</summary>
    [Fact]
    public void IncompleteHemisphereBatchPreservesHistory()
    {
        EnsureContextValid();
        using var fixture = new DynamicSurfaceLightingFixture((id, x, y, z) =>
            new(x <= 0 || x >= 2 ? 2u | id << 2 : 1u,
                LumonSceneOccupancyPacking.PackClamped(32, 0, 0, 0), 0));
        Assert.True(fixture.Page.Capture(fixture.Geometry.Scene));
        fixture.Page.Relight(fixture.Geometry.Scene, steps: 2, rays: 1);
        float[] firstDirections = fixture.Page.ReadLighting();
        fixture.Page.ResetLighting();
        Assert.False(fixture.Page.Relight(fixture.Geometry.Scene, steps: 2, rays: 16));
        float[] batches = fixture.Page.ReadLighting();
        // r=0 is identical in both dispatches. A successful first ray and rejected
        // full batch proves the same texel contains both resolved and unresolved directions.
        bool mixed = false;
        for (int i = 0; i < batches.Length; i += 4)
        {
            if (firstDirections[i + 3] == 1f && batches[i + 3] == 0f)
            {
                mixed = true;
                for (int channel = 0; channel < 3; channel++) Assert.Equal(0f, batches[i + channel]);
            }
        }
        Assert.True(mixed, "Fixture must exercise a mixed resolved/unresolved hemisphere batch.");
    }

    /// <summary>A saturated history must not add energy when another equal-light batch arrives.</summary>
    [Fact]
    public void CappedHistoryPreservesConstantIrradiance()
    {
        EnsureContextValid();
        using var fixture = new DynamicSurfaceLightingFixture(32);
        Assert.True(fixture.Page.Capture(fixture.Geometry.Scene));
        float[] history = Enumerable.Range(0, 64).SelectMany(_ => new float[] { 32, 32, 32, 1024 }).ToArray();
        using (GlStateCache.Current.BindTextureScope(TextureTarget.Texture2DArray, 0, fixture.Page.IrradianceAtlas.TextureId))
            GL.TexSubImage3D(TextureTarget.Texture2DArray, 0, 0, 0, 0, 8, 8, 1, PixelFormat.Rgba, PixelType.Float, history);
        Assert.True(fixture.Page.Relight(fixture.Geometry.Scene));
        float[] result = fixture.Page.ReadLighting();
        for (int i = 0; i < result.Length; i += 4)
        {
            for (int channel = 0; channel < 3; channel++) Assert.Equal(32f, result[i + channel]);
            Assert.Equal(1024f, result[i + 3]);
        }
    }
    #endregion
}
