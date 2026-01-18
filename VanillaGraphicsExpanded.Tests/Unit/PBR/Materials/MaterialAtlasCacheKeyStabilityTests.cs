using System;
using System.Globalization;

using Newtonsoft.Json;

using VanillaGraphicsExpanded.LumOn;
using VanillaGraphicsExpanded.PBR.Materials;
using VanillaGraphicsExpanded.PBR.Materials.Cache;
using VanillaGraphicsExpanded.Tests.Fixtures;

using Vintagestory.API.Client;
using Vintagestory.API.Common;

namespace VanillaGraphicsExpanded.Tests.Unit.PBR.Materials;

[Collection("PbrMaterialRegistry")]
[Trait("Category", "Unit")]
public sealed class MaterialAtlasCacheKeyStabilityTests
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
    public void CacheKeys_AreStable_AcrossCalls_AndCultureIndependent()
    {
        var logger = new TestLogger();

        var src = Source(
            domain: "game",
            path: "config/vge/material_definitions.json",
            json: """
            {
              "version": 1,
              "defaults": { "scale": { "normal": 1.0, "depth": 1.0 } },
              "materials": { "stone": { "roughness": 0.2, "metallic": 0.3, "emissive": 0.4 } },
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

        CultureInfo original = CultureInfo.CurrentCulture;

        try
        {
            PbrMaterialRegistry.Instance.InitializeFromParsedSources(
                logger,
                parsedSources: new[] { src },
                textureLocations: new[] { new AssetLocation("game", "textures/block/test.png") },
                strict: true);

            var cfg = new LumOnConfig
            {
                EnableMaterialAtlasAsyncBuild = true,
                EnableNormalDepthAtlas = true
            };

            var snapshot = new AtlasSnapshot(
                Pages: new[] { new AtlasSnapshot.AtlasPage(AtlasTextureId: 101, Width: 16, Height: 16) },
                Positions: Array.Empty<TextureAtlasPosition?>(),
                ReloadIteration: 1,
                NonNullPositionCount: 1);

            var inputs = MaterialAtlasCacheKeyInputs.Create(cfg, snapshot, PbrMaterialRegistry.Instance);
            var builder = new MaterialAtlasCacheKeyBuilder();

            var rect = new AtlasRect(0, 0, 16, 16);
            var texture = new AssetLocation("game", "textures/block/test.png");

            Assert.True(PbrMaterialRegistry.Instance.TryGetMaterial(texture, out PbrMaterialDefinition def));
            Assert.True(PbrMaterialRegistry.Instance.TryGetScale(texture, out PbrOverrideScale scale));

            AtlasCacheKey k1 = builder.BuildMaterialParamsTileKey(inputs, 101, rect, texture, def, scale);

            CultureInfo.CurrentCulture = new CultureInfo("fr-FR");
            AtlasCacheKey k2 = builder.BuildMaterialParamsTileKey(inputs, 101, rect, texture, def, scale);

            Assert.Equal(k1, k2);

            // Changing config should change inputs => change key.
            var cfg2 = new LumOnConfig
            {
                EnableMaterialAtlasAsyncBuild = false,
                EnableNormalDepthAtlas = true
            };

            var inputs2 = MaterialAtlasCacheKeyInputs.Create(cfg2, snapshot, PbrMaterialRegistry.Instance);
            AtlasCacheKey k3 = builder.BuildMaterialParamsTileKey(inputs2, 101, rect, texture, def, scale);

            Assert.NotEqual(k1, k3);
        }
        finally
        {
            CultureInfo.CurrentCulture = original;
            PbrMaterialRegistry.Instance.Clear();
        }
    }
}
