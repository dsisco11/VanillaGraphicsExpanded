using System;

using Newtonsoft.Json;

using VanillaGraphicsExpanded.PBR.Materials;
using VanillaGraphicsExpanded.Tests.Fixtures;

using Vintagestory.API.Client;
using Vintagestory.API.Common;

namespace VanillaGraphicsExpanded.Tests.Unit.PBR.Materials;

[Collection("PbrMaterialRegistry")]
[Trait("Category", "Unit")]
public sealed class MaterialAtlasBuildPlannerDeterminismTests
{
    private static PbrMaterialDefinitionsSource Source(string domain, string path, string json)
    {
        var file = JsonConvert.DeserializeObject<PbrMaterialDefinitionsJsonFile>(json)
            ?? throw new InvalidOperationException("Failed to deserialize json");

        return new PbrMaterialDefinitionsSource(
            Domain: domain,
            Location: new AssetLocation(domain, path),
            File: file);
    }

    [Fact]
    public void CreatePlan_IsDeterministic_WhenInputsAreTheSame()
    {
        var logger = new TestLogger();

        var src = Source(
            domain: "game",
            path: "config/vge/material_definitions.json",
            json: """
            {
              "version": 1,
              "defaults": { "scale": { "normal": 1.0, "depth": 1.0 } },
              "materials": { "stone": { "roughness": 0.2 } },
              "mapping": [
                {
                  "id": "rule",
                  "priority": 0,
                  "match": { "glob": "assets/game/textures/block/test.png" },
                  "values": { "material": "stone" }
                }
              ]
            }
            """
        );

        try
        {
            PbrMaterialRegistry.Instance.InitializeFromParsedSources(
                logger,
                parsedSources: new[] { src },
                textureLocations: new[] { new AssetLocation("game", "textures/block/test.png") },
                strict: true);

            var snapshot = new AtlasSnapshot(
                Pages: new[] { new AtlasSnapshot.AtlasPage(AtlasTextureId: 101, Width: 16, Height: 16) },
                Positions: Array.Empty<TextureAtlasPosition?>(),
                ReloadIteration: 1,
                NonNullPositionCount: 1);

            var pos = new TextureAtlasPosition
            {
                atlasTextureId = 101,
                x1 = 0f,
                y1 = 0f,
                x2 = 1f,
                y2 = 1f
            };

            TextureAtlasPosition? TryGet(AssetLocation loc)
                => loc.Domain == "game" && loc.Path == "block/test" ? pos : null;

            var planner = new MaterialAtlasBuildPlanner();

            // Same set, different order.
            AssetLocation[] assetsA = [
                new AssetLocation("game", "textures/block/test.png"),
                new AssetLocation("game", "textures/block/zzz.png")
            ];

            AssetLocation[] assetsB = [
                new AssetLocation("game", "textures/block/zzz.png"),
                new AssetLocation("game", "textures/block/test.png")
            ];

            AtlasBuildPlan planA = planner.CreatePlan(snapshot, TryGet, PbrMaterialRegistry.Instance, assetsA, enableNormalDepth: true);
            AtlasBuildPlan planB = planner.CreatePlan(snapshot, TryGet, PbrMaterialRegistry.Instance, assetsB, enableNormalDepth: true);

            Assert.Equal(planA.Pages, planB.Pages);
            Assert.Equal(planA.MaterialParamsTiles, planB.MaterialParamsTiles);
            Assert.Equal(planA.MaterialParamsOverrides, planB.MaterialParamsOverrides);
            Assert.Equal(planA.NormalDepthTiles, planB.NormalDepthTiles);
            Assert.Equal(planA.NormalDepthOverrides, planB.NormalDepthOverrides);
            Assert.Equal(planA.Stats, planB.Stats);
        }
        finally
        {
            PbrMaterialRegistry.Instance.Clear();
        }
    }
}
