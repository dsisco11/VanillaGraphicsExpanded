using System.Numerics;
using VanillaGraphicsExpanded.PBR.Materials;
using Vintagestory.API.Common;

using Assertions = VanillaGraphicsExpanded.Tests.Support.GPU.Observations;

namespace VanillaGraphicsExpanded.Tests.Integration.Water;


/// <summary>Checks coefficient publication independently of BRDF caches and retires metadata with atlas pages.</summary>
[Collection("GPU")]
[Trait("Category", "GpuIntegration")]
public sealed class WaterMediumAtlasTests(HeadlessGLFixture fixture) : RenderTestBase(fixture)
{


    #region Public API
    /// <summary>Preserves explicit zero density, authored SI coefficients and neutral gaps through rebuild and resize.</summary>
    [Fact]
    public void RecordsPublishAndRetireWithAtlasGeneration()
    {
        // Arrange: establish this scenario's controlled inputs and starting state.

        EnsureContextValid();
        using var store = new MaterialAtlasTextureStore();
        var medium = new WaterMedium(new(.1f, .2f, .3f), new(.4f, .5f, .6f), 2, .7f);
        var definition = new PbrMaterialDefinition(.1f, 0, 0, default, PbrOverrideScale.Identity, 0, null,
            Transmission: 1, WaterMedium: medium);
        var tile = new AtlasBuildPlan.MaterialParamsTileJob(7, new(1, 1, 2, 2),
            new AssetLocation("game", "textures/block/water.png"), definition, PbrOverrideScale.Identity, 0);
        var plan = new AtlasBuildPlan(new AtlasSnapshot([], [], 0, 0), [new(7, 4, 4)], [tile], [], [], [], default);
        store.SyncToAtlasPages([(7, 4, 4)], false);
        // Act: publish water-medium records for the authored atlas generation.
        store.UpdateWaterMedium(plan);
        // Assert: published indices and two-texel coefficient records retain zero density and neutral gaps across rebuilds.
        Assert.True(store.TryGetWaterMediumTextures(7, out var page));
        var indices = new float[16];
        for (int y = 0; y < 4; y++) for (int x = 0; x < 4; x++)
            indices[y*4+x]=x is 1 or 2 && y is 1 or 2?1:0;
        ImageAssertions.Comparisons.Compare(page!.Indices,indices).AssertEqual();
        var records = page.Records.ReadPixelsRegion(0,0,2,1);
        Assert.Equal(medium.AbsorptionPerMetre.X, records[0]);
        Assert.Equal(medium.AbsorptionPerMetre.Y, records[1]);
        Assert.Equal(medium.AbsorptionPerMetre.Z, records[2]);
        Assert.Equal(.7f, records[3]);
        Assert.Equal(.8f, records[4]);
        Assert.Equal(1, records[5]);
        Assert.Equal(1.2f, records[6]);
        store.UpdateWaterMedium(plan with { MaterialParamsTiles = [tile with {
            Definition = definition with { WaterMedium = medium with { Density = 0 } } }] });
        Assert.True(store.TryGetWaterMediumTextures(7, out page));
        records = page!.Records.ReadPixelsRegion(0,0,2,1);
        Assert.Equal(Vector3.Zero, new(records[0], records[1], records[2]));
        Assert.Equal(Vector3.Zero, new(records[4], records[5], records[6]));
        store.SyncToAtlasPages([(7, 8, 8)], false);
        Assert.False(store.TryGetWaterMediumTextures(7, out _));
        store.UpdateWaterMedium(plan);
        Assert.False(store.TryGetWaterMediumTextures(7, out _));
        store.UpdateWaterMedium(plan with { MaterialParamsTiles = [] });
        Assert.False(store.TryGetWaterMediumTextures(7, out _));
        store.UpdateWaterMedium(plan with { Pages = [new(7, 8, 8)] });
        Assert.True(store.TryGetWaterMediumTextures(7, out _));
        store.SyncToAtlasPages([], false);
        Assert.False(store.TryGetWaterMediumTextures(7, out _));
    }

    /// <summary>Two-texel records cross table rows without rounding away tile indices.</summary>
    [Fact]
    public void CompactRecordsAddressBeyondFirstRow()
    {
        // Arrange: establish this scenario's controlled inputs and starting state.

        EnsureContextValid();
        using var store = new MaterialAtlasTextureStore();
        var definition = new PbrMaterialDefinition(.1f, 0, 0, default, PbrOverrideScale.Identity, 0, null,
            Transmission: 1, WaterMedium: WaterMedium.Clear);
        var tiles = Enumerable.Range(0, 129).Select(index => new AtlasBuildPlan.MaterialParamsTileJob(
            7, new(index, 0, 1, 1), new AssetLocation("game", $"textures/block/{index}.png"),
            definition with { WaterMedium = new(Vector3.Zero, new Vector3(index), 1, 0) },
            PbrOverrideScale.Identity, 0)).ToArray();
        var plan = new AtlasBuildPlan(new AtlasSnapshot([], [], 0, 0), [new(7, 256, 1)], tiles, [], [], [], default);
        store.SyncToAtlasPages([(7, 256, 1)], false);
        // Act: publish compact records spanning two texture rows.
        store.UpdateWaterMedium(plan);
        // Assert: table dimensions and indexed values preserve records beyond the first row.
        Assert.True(store.TryGetWaterMediumTextures(7, out var page));
        Assert.Equal(256, page!.Records.Width);
        Assert.Equal(2, page.Records.Height);
        Assert.Equal(129, Assertions.GpuLocalRead.Scalar(page.Indices, 128));
        Assert.Equal(128, Assertions.GpuLocalRead.Scalar(page.Records, 128 * 8 + 4));
    }
    #endregion
    #region Private
    /// <summary>Prepares this suite's declared observation programs before subject execution.</summary>
    private protected override void PrepareObservations(VanillaGraphicsExpanded.Tests.Support.GPU.Observations.GpuObservationPrograms programs)
    {
        base.PrepareObservations(programs);
        programs.Prepare("compare", 0, 0);
    }
    #endregion
}
