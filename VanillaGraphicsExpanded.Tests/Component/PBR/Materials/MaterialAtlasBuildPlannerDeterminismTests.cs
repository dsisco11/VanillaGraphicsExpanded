using System;
using VanillaGraphicsExpanded.Tests.Support.Cache;
using Newtonsoft.Json;
using VanillaGraphicsExpanded.PBR.Materials;
using Vintagestory.API.Client;
using Vintagestory.API.Common;

namespace VanillaGraphicsExpanded.Tests.Component.PBR.Materials;

/// <summary>Verifies deterministic atlas plans across equivalent asset orderings.</summary>
[Collection("PbrMaterialRegistry")]
[Trait("Category", "Unit")]
public sealed class MaterialAtlasBuildPlannerDeterminismTests
{
    #region Public API
    /// <summary>Equivalent asset sets produce the same atlas pages, material data, and planner statistics.</summary>
    [Fact]
    public void CreatePlan_IsDeterministic_WhenInputsAreTheSame()
    {
        // Arrange: load one material mapping into the registry used by the atlas planner.
        var logger = new TestLogger();
        var source = Source("game", "config/vge/material_definitions.json", """
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
            """);
        var snapshot = new AtlasSnapshot(
            Pages: [new AtlasSnapshot.AtlasPage(AtlasTextureId: 101, Width: 16, Height: 16)],
            Positions: Array.Empty<TextureAtlasPosition?>(),
            ReloadIteration: 1,
            NonNullPositionCount: 1);
        var position = new TextureAtlasPosition { atlasTextureId = 101, x1 = 0f, y1 = 0f, x2 = 1f, y2 = 1f };
        /// <summary>Resolves only the fixture texture to its fixed atlas position.</summary>
        TextureAtlasPosition? TryGet(AssetLocation location)
            => location.Domain == "game" && location.Path == "block/test" ? position : null;
        AssetLocation[] assetsA = [
            new("game", "textures/block/test.png"),
            new("game", "textures/block/zzz.png")
        ];
        AssetLocation[] assetsB = [
            new("game", "textures/block/zzz.png"),
            new("game", "textures/block/test.png")
        ];
        var planner = new MaterialAtlasBuildPlanner();

        try
        {
            PbrMaterialRegistry.Instance.InitializeFromParsedSources(
                logger,
                parsedSources: [source],
                textureLocations: [new AssetLocation("game", "textures/block/test.png")],
                strict: true);

            // Act: plan the same assets in opposite enumeration orders.
            AtlasBuildPlan planA = planner.CreatePlan(snapshot, TryGet, PbrMaterialRegistry.Instance, assetsA, enableNormalDepth: true);
            AtlasBuildPlan planB = planner.CreatePlan(snapshot, TryGet, PbrMaterialRegistry.Instance, assetsB, enableNormalDepth: true);

            // Assert: ordering does not change pages, tiles, overrides, or accounting.
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
    #endregion

    #region Private
    /// <summary>Parses a fixture definition file into a registry source.</summary>
    private static PbrMaterialDefinitionsSource Source(string domain, string path, string json)
    {
        var file = JsonConvert.DeserializeObject<PbrMaterialDefinitionsJsonFile>(json)
            ?? throw new InvalidOperationException("Failed to deserialize json");
        return new PbrMaterialDefinitionsSource(domain, new AssetLocation(domain, path), file);
    }
    #endregion
}


