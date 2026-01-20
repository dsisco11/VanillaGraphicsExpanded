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

            var cfg = new LumOnConfig();
            cfg.MaterialAtlas.EnableAsync = true;
            cfg.MaterialAtlas.EnableNormalMaps = true;

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

            // Async scheduling is a performance concern and must not affect cache keys.
            var cfg2 = new LumOnConfig();
            cfg2.MaterialAtlas.EnableAsync = false;
            cfg2.MaterialAtlas.EnableNormalMaps = true;

            var inputs2 = MaterialAtlasCacheKeyInputs.Create(cfg2, snapshot, PbrMaterialRegistry.Instance);
            AtlasCacheKey k3 = builder.BuildMaterialParamsTileKey(inputs2, 101, rect, texture, def, scale);

            Assert.Equal(k1, k3);

            // Output-affecting config should change inputs => change key.
            var cfg3 = new LumOnConfig();
            cfg3.MaterialAtlas.EnableAsync = true;
            cfg3.MaterialAtlas.EnableNormalMaps = false;

            var inputs3 = MaterialAtlasCacheKeyInputs.Create(cfg3, snapshot, PbrMaterialRegistry.Instance);
            AtlasCacheKey k4 = builder.BuildMaterialParamsTileKey(inputs3, 101, rect, texture, def, scale);
            Assert.NotEqual(k1, k4);
        }
        finally
        {
            CultureInfo.CurrentCulture = original;
            PbrMaterialRegistry.Instance.Clear();
        }
    }

    [Fact]
    public void CacheKeys_Change_WhenSnapshotFingerprintChanges()
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

        try
        {
            PbrMaterialRegistry.Instance.InitializeFromParsedSources(
                logger,
                parsedSources: new[] { src },
                textureLocations: new[] { new AssetLocation("game", "textures/block/test.png") },
                strict: true);

            var cfg = new LumOnConfig();
            cfg.MaterialAtlas.EnableAsync = true;
            cfg.MaterialAtlas.EnableNormalMaps = true;

            var snapshotA = new AtlasSnapshot(
                Pages: new[] { new AtlasSnapshot.AtlasPage(AtlasTextureId: 101, Width: 16, Height: 16) },
                Positions: Array.Empty<TextureAtlasPosition?>(),
                ReloadIteration: 1,
                NonNullPositionCount: 1);

            var snapshotB = new AtlasSnapshot(
                Pages: new[] { new AtlasSnapshot.AtlasPage(AtlasTextureId: 101, Width: 16, Height: 16) },
                Positions: Array.Empty<TextureAtlasPosition?>(),
                ReloadIteration: 2,
                NonNullPositionCount: 1);

            var inputsA = MaterialAtlasCacheKeyInputs.Create(cfg, snapshotA, PbrMaterialRegistry.Instance);
            var inputsB = MaterialAtlasCacheKeyInputs.Create(cfg, snapshotB, PbrMaterialRegistry.Instance);

            var builder = new MaterialAtlasCacheKeyBuilder();
            var rect = new AtlasRect(0, 0, 16, 16);
            var texture = new AssetLocation("game", "textures/block/test.png");

            Assert.True(PbrMaterialRegistry.Instance.TryGetMaterial(texture, out PbrMaterialDefinition def));
            Assert.True(PbrMaterialRegistry.Instance.TryGetScale(texture, out PbrOverrideScale scale));

            AtlasCacheKey kA = builder.BuildMaterialParamsTileKey(inputsA, 101, rect, texture, def, scale);
            AtlasCacheKey kB = builder.BuildMaterialParamsTileKey(inputsB, 101, rect, texture, def, scale);

            Assert.NotEqual(kA, kB);
        }
        finally
        {
            PbrMaterialRegistry.Instance.Clear();
        }
    }

    [Fact]
    public void OverrideKeys_Change_WhenOverrideAssetOrScaleChanges()
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
              "mapping": []
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

            var cfg = new LumOnConfig();
            cfg.MaterialAtlas.EnableAsync = true;
            cfg.MaterialAtlas.EnableNormalMaps = true;

            var snapshot = new AtlasSnapshot(
                Pages: new[] { new AtlasSnapshot.AtlasPage(AtlasTextureId: 101, Width: 16, Height: 16) },
                Positions: Array.Empty<TextureAtlasPosition?>(),
                ReloadIteration: 1,
                NonNullPositionCount: 1);

            var inputs = MaterialAtlasCacheKeyInputs.Create(cfg, snapshot, PbrMaterialRegistry.Instance);
            var builder = new MaterialAtlasCacheKeyBuilder();

            var rect = new AtlasRect(0, 0, 16, 16);
            var targetTexture = new AssetLocation("game", "textures/block/test.png");

            // Material params override key
            var ov1 = new AssetLocation("game", "textures/block/test_override1.png");
            var ov2 = new AssetLocation("game", "textures/block/test_override2.png");
            var scale1 = new PbrOverrideScale(Roughness: 1f, Metallic: 1f, Emissive: 1f, Normal: 1f, Depth: 1f);
            var scale2 = new PbrOverrideScale(Roughness: 0.9f, Metallic: 1f, Emissive: 1f, Normal: 1f, Depth: 1f);

            AtlasCacheKey mpA = builder.BuildMaterialParamsOverrideTileKey(inputs, 101, rect, targetTexture, ov1, scale1, ruleId: "r", ruleSource: null);
            AtlasCacheKey mpB = builder.BuildMaterialParamsOverrideTileKey(inputs, 101, rect, targetTexture, ov2, scale1, ruleId: "r", ruleSource: null);
            AtlasCacheKey mpC = builder.BuildMaterialParamsOverrideTileKey(inputs, 101, rect, targetTexture, ov1, scale2, ruleId: "r", ruleSource: null);

            Assert.NotEqual(mpA, mpB);
            Assert.NotEqual(mpA, mpC);

            // Normal+depth override key
            AtlasCacheKey ndA = builder.BuildNormalDepthOverrideTileKey(inputs, 101, rect, targetTexture, ov1, normalScale: 1f, depthScale: 1f, ruleId: "r", ruleSource: null);
            AtlasCacheKey ndB = builder.BuildNormalDepthOverrideTileKey(inputs, 101, rect, targetTexture, ov2, normalScale: 1f, depthScale: 1f, ruleId: "r", ruleSource: null);
            AtlasCacheKey ndC = builder.BuildNormalDepthOverrideTileKey(inputs, 101, rect, targetTexture, ov1, normalScale: 0.8f, depthScale: 1f, ruleId: "r", ruleSource: null);

            Assert.NotEqual(ndA, ndB);
            Assert.NotEqual(ndA, ndC);
        }
        finally
        {
            PbrMaterialRegistry.Instance.Clear();
        }
    }

    [Fact]
    public void CacheKeys_DoNotChange_WhenAsyncBudgetsChange()
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

            var rect = new AtlasRect(0, 0, 16, 16);
            var texture = new AssetLocation("game", "textures/block/test.png");

            Assert.True(PbrMaterialRegistry.Instance.TryGetMaterial(texture, out PbrMaterialDefinition def));
            Assert.True(PbrMaterialRegistry.Instance.TryGetScale(texture, out PbrOverrideScale scale));

            var builder = new MaterialAtlasCacheKeyBuilder();

            var cfgA = new LumOnConfig();
            cfgA.MaterialAtlas.EnableAsync = true;
            cfgA.MaterialAtlas.EnableNormalMaps = true;
            cfgA.MaterialAtlas.AsyncBudgetMs = 1.5f;
            cfgA.MaterialAtlas.AsyncMaxUploadsPerFrame = 8;
            cfgA.MaterialAtlas.AsyncMaxJobsPerFrame = 2;
            cfgA.MaterialAtlas.AsyncBudgetMs = 0.75f;
            cfgA.MaterialAtlas.AsyncMaxUploadsPerFrame = 2;

            var cfgB = new LumOnConfig();
            cfgB.MaterialAtlas.EnableAsync = true;
            cfgB.MaterialAtlas.EnableNormalMaps = true;
            cfgB.MaterialAtlas.AsyncBudgetMs = 3.0f;
            cfgB.MaterialAtlas.AsyncMaxUploadsPerFrame = 32;
            cfgB.MaterialAtlas.AsyncMaxJobsPerFrame = 8;
            cfgB.MaterialAtlas.AsyncBudgetMs = 2.0f;
            cfgB.MaterialAtlas.AsyncMaxUploadsPerFrame = 8;

            var inputsA = MaterialAtlasCacheKeyInputs.Create(cfgA, snapshot, PbrMaterialRegistry.Instance);
            var inputsB = MaterialAtlasCacheKeyInputs.Create(cfgB, snapshot, PbrMaterialRegistry.Instance);

            AtlasCacheKey kA = builder.BuildMaterialParamsTileKey(inputsA, 101, rect, texture, def, scale);
            AtlasCacheKey kB = builder.BuildMaterialParamsTileKey(inputsB, 101, rect, texture, def, scale);

            Assert.Equal(kA, kB);
        }
        finally
        {
            PbrMaterialRegistry.Instance.Clear();
        }
    }
}
