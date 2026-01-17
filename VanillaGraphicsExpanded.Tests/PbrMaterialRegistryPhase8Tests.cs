using Newtonsoft.Json;

using VanillaGraphicsExpanded.PBR.Materials;
using VanillaGraphicsExpanded.Tests.Fixtures;

using Vintagestory.API.Common;

namespace VanillaGraphicsExpanded.Tests;

[Collection("PbrMaterialRegistry")]
public sealed class PbrMaterialRegistryPhase8Tests
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

    private static IReadOnlyList<AssetLocation> Textures(params string[] paths)
    {
        return paths.Select(p => new AssetLocation("game", p)).ToArray();
    }

    [Fact]
    public void Defaults_ApplyCorrectly()
    {
        var logger = new TestLogger();

        var src = Source(
            domain: "game",
            path: "materials/pbr_material_definitions.json",
            json: """
            {
              "version": 1,
              "defaults": { "roughness": 0.2, "metallic": 0.3, "emissive": 0.4 },
              "materials": { "stone": { } },
              "mapping": []
            }
            """);

        PbrMaterialRegistry.Instance.InitializeFromParsedSources(
            logger,
            parsedSources: new[] { src },
            textureLocations: Array.Empty<AssetLocation>(),
            strict: true);

        Assert.True(PbrMaterialRegistry.Instance.TryGetMaterial("game:stone", out var def));
        Assert.Equal(0.2f, def.Roughness, 3);
        Assert.Equal(0.3f, def.Metallic, 3);
        Assert.Equal(0.4f, def.Emissive, 3);
    }

    [Fact]
    public void MaterialOverrides_ApplyCorrectly()
    {
        var logger = new TestLogger();

        var src = Source(
            domain: "game",
            path: "materials/pbr_material_definitions.json",
            json: """
            {
              "version": 1,
              "defaults": { "roughness": 0.2, "metallic": 0.3, "emissive": 0.4 },
              "materials": { "stone": { "roughness": 0.9 } },
              "mapping": []
            }
            """);

        PbrMaterialRegistry.Instance.InitializeFromParsedSources(
            logger,
            parsedSources: new[] { src },
            textureLocations: Array.Empty<AssetLocation>(),
            strict: true);

        Assert.True(PbrMaterialRegistry.Instance.TryGetMaterial("game:stone", out var def));
        Assert.Equal(0.9f, def.Roughness, 3);
        Assert.Equal(0.3f, def.Metallic, 3);
        Assert.Equal(0.4f, def.Emissive, 3);
    }

    [Fact]
    public void MaterialIdCollision_HigherPriorityWins()
    {
        var logger = new TestLogger();

        var low = Source(
            domain: "a",
            path: "materials/pbr_material_definitions.json",
            json: """
            {
              "version": 1,
              "materials": { "shared:mat": { "roughness": 0.1, "priority": 0 } },
              "mapping": []
            }
            """);

        var high = Source(
            domain: "b",
            path: "materials/pbr_material_definitions.json",
            json: """
            {
              "version": 1,
              "materials": { "shared:mat": { "roughness": 0.8, "priority": 10 } },
              "mapping": []
            }
            """);

        PbrMaterialRegistry.Instance.InitializeFromParsedSources(
            logger,
            parsedSources: new[] { low, high },
            textureLocations: Array.Empty<AssetLocation>(),
            strict: true);

        Assert.True(PbrMaterialRegistry.Instance.TryGetMaterial("shared:mat", out var def));
        Assert.Equal(0.8f, def.Roughness, 3);
    }

    [Fact]
    public void MaterialIdCollision_Tie_LaterDeterministicOrderWins()
    {
        var logger = new TestLogger();

        var first = Source(
            domain: "a",
            path: "materials/pbr_material_definitions.json",
            json: """
            {
              "version": 1,
              "materials": { "shared:mat": { "roughness": 0.1, "priority": 5 } },
              "mapping": []
            }
            """);

        var second = Source(
            domain: "b",
            path: "materials/pbr_material_definitions.json",
            json: """
            {
              "version": 1,
              "materials": { "shared:mat": { "roughness": 0.9, "priority": 5 } },
              "mapping": []
            }
            """);

        PbrMaterialRegistry.Instance.InitializeFromParsedSources(
            logger,
            parsedSources: new[] { first, second },
            textureLocations: Array.Empty<AssetLocation>(),
            strict: true);

        Assert.True(PbrMaterialRegistry.Instance.TryGetMaterial("shared:mat", out var def));
        Assert.Equal(0.9f, def.Roughness, 3);
    }

    [Fact]
    public void MappingRuleOrdering_HigherPriorityWins()
    {
        var logger = new TestLogger();

        var src = Source(
            domain: "game",
            path: "materials/pbr_material_definitions.json",
            json: """
            {
              "version": 1,
              "materials": {
                "a": { "roughness": 0.1 },
                "b": { "roughness": 0.9 }
              },
              "mapping": [
                {
                  "id": "low",
                  "priority": 0,
                  "match": { "glob": "assets/game/textures/block/test.png" },
                  "values": { "material": "a" }
                },
                {
                  "id": "high",
                  "priority": 1,
                  "match": { "glob": "assets/game/textures/block/test.png" },
                  "values": { "material": "b" }
                }
              ]
            }
            """);

        var textures = Textures("textures/block/test.png");

        PbrMaterialRegistry.Instance.InitializeFromParsedSources(
            logger,
            parsedSources: new[] { src },
            textureLocations: textures,
            strict: true);

        Assert.True(PbrMaterialRegistry.Instance.TryGetMaterialId(new AssetLocation("game", "textures/block/test.png"), out var materialId));
        Assert.Equal("game:b", materialId);
    }

    [Fact]
    public void Mapping_Works_WhenTextureLocationsAreRelativeToTexturesFolder()
    {
        // This simulates a real-world AssetManager behavior where GetLocations("textures/")
        // returns paths relative to the base folder (e.g. "block/test.png").
        var logger = new TestLogger();

        var src = Source(
            domain: "game",
            path: "materials/pbr_material_definitions.json",
            json: """
            {
              "version": 1,
              "materials": {
                "a": { "roughness": 0.1 }
              },
              "mapping": [
                {
                  "id": "rule",
                  "priority": 0,
                  "match": { "glob": "assets/game/textures/block/test.png" },
                  "values": { "material": "a" }
                }
              ]
            }
            """);

        var textures = Textures("block/test.png");

        PbrMaterialRegistry.Instance.InitializeFromParsedSources(
            logger,
            parsedSources: new[] { src },
            textureLocations: textures,
            strict: true);

        Assert.True(PbrMaterialRegistry.Instance.TryGetMaterialId(new AssetLocation("game", "textures/block/test.png"), out var materialId));
        Assert.Equal("game:a", materialId);
    }

    [Fact]
    public void MappingRuleOrdering_Tie_FirstRuleWins()
    {
        var logger = new TestLogger();

        var src = Source(
            domain: "game",
            path: "materials/pbr_material_definitions.json",
            json: """
            {
              "version": 1,
              "materials": {
                "a": { "roughness": 0.1 },
                "b": { "roughness": 0.9 }
              },
              "mapping": [
                {
                  "id": "first",
                  "priority": 0,
                  "match": { "glob": "assets/game/textures/block/test.png" },
                  "values": { "material": "a" }
                },
                {
                  "id": "second",
                  "priority": 0,
                  "match": { "glob": "assets/game/textures/block/test.png" },
                  "values": { "material": "b" }
                }
              ]
            }
            """);

        var textures = Textures("textures/block/test.png");

        PbrMaterialRegistry.Instance.InitializeFromParsedSources(
            logger,
            parsedSources: new[] { src },
            textureLocations: textures,
            strict: true);

        Assert.True(PbrMaterialRegistry.Instance.TryGetMaterialId(new AssetLocation("game", "textures/block/test.png"), out var materialId));
        Assert.Equal("game:a", materialId);
    }

    [Fact]
    public void MappingRuleOverrides_AreParsedAndAttachedPerTexture()
    {
        var logger = new TestLogger();

        var src = Source(
            domain: "game",
            path: "materials/pbr_material_definitions.json",
            json: """
            {
              "version": 1,
              "materials": { "a": { "roughness": 0.1 } },
              "mapping": [
                {
                  "id": "rule",
                  "priority": 0,
                  "match": { "glob": "assets/game/textures/block/test.png" },
                  "values": {
                    "material": "a",
                    "overrides": {
                      "materialParams": "textures/vge/params/test_params.png",
                      "normalHeight": "mymod:textures/vge/nh/test_nh.dds"
                    }
                  }
                }
              ]
            }
            """);

        var textures = Textures("textures/block/test.png");

        PbrMaterialRegistry.Instance.InitializeFromParsedSources(
            logger,
            parsedSources: new[] { src },
            textureLocations: textures,
            strict: true);

        var key = new AssetLocation("game", "textures/block/test.png");
        Assert.True(PbrMaterialRegistry.Instance.OverridesByTexture.TryGetValue(key, out PbrMaterialTextureOverrides overrides));

        Assert.Equal("rule", overrides.RuleId);
        Assert.Equal(new AssetLocation("game", "materials/pbr_material_definitions.json"), overrides.RuleSource);

        Assert.NotNull(overrides.MaterialParams);
        Assert.Equal("game", overrides.MaterialParams!.Domain);
        Assert.Equal("textures/vge/params/test_params.png", overrides.MaterialParams.Path);

        Assert.NotNull(overrides.NormalHeight);
        Assert.Equal("mymod", overrides.NormalHeight!.Domain);
        Assert.Equal("textures/vge/nh/test_nh.dds", overrides.NormalHeight.Path);
    }

    [Fact]
    public void MappingRuleOrdering_Tie_FirstRuleWins_AndItsOverridesAreUsed()
    {
        var logger = new TestLogger();

        var src = Source(
            domain: "game",
            path: "materials/pbr_material_definitions.json",
            json: """
            {
              "version": 1,
              "materials": {
                "a": { "roughness": 0.1 },
                "b": { "roughness": 0.9 }
              },
              "mapping": [
                {
                  "id": "first",
                  "priority": 0,
                  "match": { "glob": "assets/game/textures/block/test.png" },
                  "values": {
                    "material": "a",
                    "overrides": { "materialParams": "textures/vge/params/first.png" }
                  }
                },
                {
                  "id": "second",
                  "priority": 0,
                  "match": { "glob": "assets/game/textures/block/test.png" },
                  "values": {
                    "material": "b",
                    "overrides": { "materialParams": "textures/vge/params/second.png" }
                  }
                }
              ]
            }
            """);

        var textures = Textures("textures/block/test.png");

        PbrMaterialRegistry.Instance.InitializeFromParsedSources(
            logger,
            parsedSources: new[] { src },
            textureLocations: textures,
            strict: true);

        var key = new AssetLocation("game", "textures/block/test.png");

        Assert.True(PbrMaterialRegistry.Instance.TryGetMaterialId(key, out var materialId));
        Assert.Equal("game:a", materialId);

        Assert.True(PbrMaterialRegistry.Instance.OverridesByTexture.TryGetValue(key, out PbrMaterialTextureOverrides overrides));
        Assert.Equal("first", overrides.RuleId);
        Assert.Equal(new AssetLocation("game", "textures/vge/params/first.png"), overrides.MaterialParams);
    }

    [Fact]
    public void InvalidOverrideStrings_WarnAndAreTreatedAsNoOverride()
    {
        var logger = new TestLogger();
        var warnings = new List<string>();

        logger.EntryAdded += (t, fmt, args) =>
        {
            if (t == EnumLogType.Warning)
            {
                warnings.Add(string.Format(fmt, args));
            }
        };

        var src = Source(
            domain: "game",
            path: "materials/pbr_material_definitions.json",
            json: """
            {
              "version": 1,
              "materials": { "a": { "roughness": 0.1 } },
              "mapping": [
                {
                  "id": "rule",
                  "priority": 0,
                  "match": { "glob": "assets/game/textures/block/*.png" },
                  "values": {
                    "material": "a",
                    "overrides": { "materialParams": "mymod:textures/vge/params/bad.gif" }
                  }
                }
              ]
            }
            """);

        var textures = Textures("textures/block/test.png", "textures/block/test2.png");

        PbrMaterialRegistry.Instance.InitializeFromParsedSources(
            logger,
            parsedSources: new[] { src },
            textureLocations: textures,
            strict: true);

        Assert.Empty(PbrMaterialRegistry.Instance.OverridesByTexture);

        // Warning is emitted once per rule/kind (not once per matching texture).
        Assert.Single(warnings);
        Assert.Contains("PBR override ignored", warnings[0]);
        Assert.Contains("unsupported extension", warnings[0]);
    }

      [Fact]
      public void OverridesScale_Missing_DefaultsToIdentity_NoWarnings()
      {
        var logger = new TestLogger();
        var warnings = new List<string>();

        logger.EntryAdded += (t, fmt, args) =>
        {
          if (t == EnumLogType.Warning)
          {
            warnings.Add(string.Format(fmt, args));
          }
        };

        var src = Source(
          domain: "game",
          path: "materials/pbr_material_definitions.json",
          json: """
          {
            "version": 1,
            "materials": { "a": { "roughness": 0.1 } },
            "mapping": [
            {
              "id": "rule",
              "priority": 0,
              "match": { "glob": "assets/game/textures/block/test.png" },
              "values": {
              "material": "a",
              "overrides": { "materialParams": "textures/vge/params/test_params.png" }
              }
            }
            ]
          }
          """
        );

        var textures = Textures("textures/block/test.png");

        PbrMaterialRegistry.Instance.InitializeFromParsedSources(
          logger,
          parsedSources: new[] { src },
          textureLocations: textures,
          strict: true);

        var key = new AssetLocation("game", "textures/block/test.png");
        Assert.True(PbrMaterialRegistry.Instance.OverridesByTexture.TryGetValue(key, out PbrMaterialTextureOverrides overrides));

        Assert.Equal(PbrOverrideScale.Identity, overrides.Scale);
        Assert.Empty(warnings);
      }

      [Fact]
      public void OverridesScale_InvalidValues_WarnAndDefaultToOne()
      {
        var logger = new TestLogger();
        var warnings = new List<string>();

        logger.EntryAdded += (t, fmt, args) =>
        {
          if (t == EnumLogType.Warning)
          {
            warnings.Add(string.Format(fmt, args));
          }
        };

        // Negative values are representable in JSON; NaN/Infinity are covered by direct object construction below.
        var src = Source(
          domain: "game",
          path: "materials/pbr_material_definitions.json",
          json: """
          {
            "version": 1,
            "materials": { "a": { "roughness": 0.1 } },
            "mapping": [
            {
              "id": "rule",
              "priority": 0,
              "match": { "glob": "assets/game/textures/block/test.png" },
              "values": {
              "material": "a",
              "overrides": {
                "materialParams": "textures/vge/params/test_params.png",
                "scale": { "roughness": -1, "metallic": 2, "emissive": 0.5 }
              }
              }
            }
            ]
          }
          """
        );

        var textures = Textures("textures/block/test.png");

        PbrMaterialRegistry.Instance.InitializeFromParsedSources(
          logger,
          parsedSources: new[] { src },
          textureLocations: textures,
          strict: true);

        var key = new AssetLocation("game", "textures/block/test.png");
        Assert.True(PbrMaterialRegistry.Instance.OverridesByTexture.TryGetValue(key, out PbrMaterialTextureOverrides overrides));

        Assert.Equal(1f, overrides.Scale.Roughness);
        Assert.Equal(2f, overrides.Scale.Metallic);
        Assert.Equal(0.5f, overrides.Scale.Emissive);

        Assert.Single(warnings);
        Assert.Contains("Invalid override scale value", warnings[0]);
        Assert.Contains("roughness", warnings[0]);
      }

      [Fact]
      public void OverridesScale_NaNAndInfinity_WarnAndDefaultToOne()
      {
        var logger = new TestLogger();
        var warnings = new List<string>();

        logger.EntryAdded += (t, fmt, args) =>
        {
          if (t == EnumLogType.Warning)
          {
            warnings.Add(string.Format(fmt, args));
          }
        };

        var file = new PbrMaterialDefinitionsJsonFile
        {
          Version = 1,
          Materials = new Dictionary<string, PbrMaterialDefinitionJson>
          {
            ["a"] = new PbrMaterialDefinitionJson { Roughness = 0.1f }
          },
          Mapping = new List<PbrMaterialMappingRuleJson>
          {
            new()
            {
              Id = "rule",
              Priority = 0,
              Match = new PbrMaterialMatchJson { Glob = "assets/game/textures/block/test.png" },
              Values = new PbrMaterialMappingValuesJson
              {
                Material = "a",
                Overrides = new PbrMaterialMappingOverridesJson
                {
                  MaterialParams = "textures/vge/params/test_params.png",
                  Scale = new PbrOverrideScaleJson
                  {
                    Roughness = float.NaN,
                    Metallic = float.PositiveInfinity,
                    Emissive = 2f,
                    Normal = 0.25f,
                    Depth = float.NegativeInfinity
                  }
                }
              }
            }
          }
        };

        var src = new PbrMaterialDefinitionsSource(
          Domain: "game",
          Location: new AssetLocation("game", "materials/pbr_material_definitions.json"),
          File: file);

        var textures = Textures("textures/block/test.png");

        PbrMaterialRegistry.Instance.InitializeFromParsedSources(
          logger,
          parsedSources: new[] { src },
          textureLocations: textures,
          strict: true);

        var key = new AssetLocation("game", "textures/block/test.png");
        Assert.True(PbrMaterialRegistry.Instance.OverridesByTexture.TryGetValue(key, out PbrMaterialTextureOverrides overrides));

        Assert.Equal(1f, overrides.Scale.Roughness);
        Assert.Equal(1f, overrides.Scale.Metallic);
        Assert.Equal(2f, overrides.Scale.Emissive);
        Assert.Equal(0.25f, overrides.Scale.Normal);
        Assert.Equal(1f, overrides.Scale.Depth);

        Assert.Single(warnings);
        Assert.Contains("Invalid override scale value", warnings[0]);
        Assert.Contains("NaN", warnings[0]);
        Assert.Contains("Infinity", warnings[0]);
      }

    [Fact]
    public void MappingRuleReferencesUnknownMaterial_WarnsAndSkipsMapping()
    {
        var logger = new TestLogger();
        bool sawUnknownMaterialWarning = false;

        logger.EntryAdded += (logType, format, _) =>
        {
            if (logType == EnumLogType.Warning && format.Contains("unknown material", StringComparison.OrdinalIgnoreCase))
            {
                sawUnknownMaterialWarning = true;
            }
        };

        var src = Source(
            domain: "game",
            path: "materials/pbr_material_definitions.json",
            json: """
            {
              "version": 1,
              "materials": {
                "a": { "roughness": 0.1 }
              },
              "mapping": [
                {
                  "id": "unknown-material",
                  "priority": 0,
                  "match": { "glob": "assets/game/textures/block/test.png" },
                  "values": { "material": "does-not-exist" }
                }
              ]
            }
            """);

        var texture = new AssetLocation("game", "textures/block/test.png");
        PbrMaterialRegistry.Instance.InitializeFromParsedSources(
            logger,
            parsedSources: new[] { src },
            textureLocations: new[] { texture },
            strict: true);

        Assert.False(PbrMaterialRegistry.Instance.TryGetMaterialId(texture, out _));
        Assert.True(sawUnknownMaterialWarning);
    }

    [Fact]
    public void LocalMaterialId_IsQualifiedAndNormalized()
    {
        var logger = new TestLogger();

        var src = Source(
            domain: "mymod",
            path: "materials/pbr_material_definitions.json",
            json: """
            {
              "version": 1,
              "materials": {
                "Stone": { "roughness": 0.42 }
              },
              "mapping": []
            }
            """);

        PbrMaterialRegistry.Instance.InitializeFromParsedSources(
            logger,
            parsedSources: new[] { src },
            textureLocations: Array.Empty<AssetLocation>(),
            strict: true);

        Assert.True(PbrMaterialRegistry.Instance.MaterialById.ContainsKey("mymod:stone"));
        Assert.False(PbrMaterialRegistry.Instance.MaterialById.ContainsKey("stone"));

        Assert.True(PbrMaterialRegistry.Instance.TryGetMaterial("MyMod:Stone", out var def));
        Assert.Equal(0.42f, def.Roughness, 3);
    }
}
