using Newtonsoft.Json;
using VanillaGraphicsExpanded.Tests.Support.Cache;

using VanillaGraphicsExpanded.PBR.Materials;

using Vintagestory.API.Common;

namespace VanillaGraphicsExpanded.Tests.Component.PBR.Materials;

/// <summary>Verifies material defaults, mapping precedence, and override admission through the registry boundary.</summary>
[Collection("PbrMaterialRegistry")]
public sealed class PbrMaterialRegistryResolutionTests : IDisposable
{
    #region Public API
    /// <summary>Starts each serialized registry case from an empty process-wide state.</summary>
    public PbrMaterialRegistryResolutionTests() => PbrMaterialRegistry.Instance.Clear();

    /// <summary>Removes registry state created by this case, including when an assertion fails.</summary>
    public void Dispose() => PbrMaterialRegistry.Instance.Clear();

    #region Material definitions and defaults
    /// <summary>Parsed defaults populate missing roughness, metallic, and emissive values.</summary>
    [Fact]
    public void Defaults_ApplyCorrectly()
    {
        // Arrange: Define material defaults and a mapped texture for registry resolution.
        var logger = new TestLogger();

        var src = Source(
            domain: "game",
            path: "config/vge/material_definitions.json",
            json: """
            {
              "version": 1,
              "defaults": { "roughness": 0.2, "metallic": 0.3, "emissive": 0.4 },
              "materials": { "stone": { } },
              "mapping": []
            }
            """);

        // Act: Initialize the registry from these parsed definitions and texture locations.
        PbrMaterialRegistry.Instance.InitializeFromParsedSources(
            logger,
            parsedSources: new[] { src },
            textureLocations: Array.Empty<AssetLocation>(),
            strict: true);

        Assert.True(PbrMaterialRegistry.Instance.TryGetMaterial("game:stone", out var def));
        // Assert: Verify the resolved material defaults for the mapped texture.
        Assert.Equal(0.2f, def.Roughness, 3);
        Assert.Equal(0.3f, def.Metallic, 3);
        Assert.Equal(0.4f, def.Emissive, 3);
    }

    /// <summary>A zero default normal scale is preserved for a mapped texture.</summary>
    [Fact]
    public void DefaultsScale_NormalZero_IsAppliedToMappedTextureScale()
    {
        // Arrange: Define a zero normal-scale default and mapped texture for registry resolution.
        var logger = new TestLogger();

        var src = Source(
            domain: "game",
            path: "config/vge/material_definitions.json",
            json: """
            {
              "version": 1,
              "defaults": {
                "scale": { "normal": 0.0, "depth": 1.0 }
              },
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

        var texture = new AssetLocation("game", "textures/block/test.png");
        // Act: Initialize the registry from these parsed definitions and texture locations.
        PbrMaterialRegistry.Instance.InitializeFromParsedSources(
            logger,
            parsedSources: new[] { src },
            textureLocations: new[] { texture },
            strict: true);

        Assert.True(PbrMaterialRegistry.Instance.TryGetScale(texture, out var scale));
        // Assert: Verify the zero normal scale on the mapped texture.
        Assert.Equal(0f, scale.Normal, 6);
        Assert.Equal(1f, scale.Depth, 6);
    }

    /// <summary>Per-material fields override defaults while unspecified values inherit.</summary>
    [Fact]
    public void MaterialOverrides_ApplyCorrectly()
    {
        // Arrange: Define material overrides and their mapped texture for registry resolution.
        var logger = new TestLogger();

        var src = Source(
            domain: "game",
            path: "config/vge/material_definitions.json",
            json: """
            {
              "version": 1,
              "defaults": { "roughness": 0.2, "metallic": 0.3, "emissive": 0.4 },
              "materials": { "stone": { "roughness": 0.9 } },
              "mapping": []
            }
            """);

        // Act: Initialize the registry from these parsed definitions and texture locations.
        PbrMaterialRegistry.Instance.InitializeFromParsedSources(
            logger,
            parsedSources: new[] { src },
            textureLocations: Array.Empty<AssetLocation>(),
            strict: true);

        Assert.True(PbrMaterialRegistry.Instance.TryGetMaterial("game:stone", out var def));
        // Assert: Verify the material and parameter texture selected by the mapping.
        Assert.Equal(0.9f, def.Roughness, 3);
        Assert.Equal(0.3f, def.Metallic, 3);
        Assert.Equal(0.4f, def.Emissive, 3);
    }

    /// <summary>A colliding material identifier selects the higher-priority definition.</summary>
    [Fact]
    public void MaterialIdCollision_HigherPriorityWins()
    {
        // Arrange: Define same-ID material definitions with different priorities for registry resolution.
        var logger = new TestLogger();

        var low = Source(
            domain: "a",
            path: "config/vge/material_definitions.json",
            json: """
            {
              "version": 1,
              "materials": { "shared:mat": { "roughness": 0.1, "priority": 0 } },
              "mapping": []
            }
            """);

        var high = Source(
            domain: "b",
            path: "config/vge/material_definitions.json",
            json: """
            {
              "version": 1,
              "materials": { "shared:mat": { "roughness": 0.8, "priority": 10 } },
              "mapping": []
            }
            """);

        // Act: Initialize the registry from these parsed definitions and texture locations.
        PbrMaterialRegistry.Instance.InitializeFromParsedSources(
            logger,
            parsedSources: new[] { low, high },
            textureLocations: Array.Empty<AssetLocation>(),
            strict: true);

        Assert.True(PbrMaterialRegistry.Instance.TryGetMaterial("shared:mat", out var def));
        // Assert: Verify the definition from the higher-priority source.
        Assert.Equal(0.8f, def.Roughness, 3);
    }

    /// <summary>Equal-priority material collisions follow deterministic source order.</summary>
    [Fact]
    public void MaterialIdCollision_Tie_LaterDeterministicOrderWins()
    {
        // Arrange: Define same-ID definitions ordered by source path for registry resolution.
        var logger = new TestLogger();

        var first = Source(
            domain: "a",
            path: "config/vge/material_definitions.json",
            json: """
            {
              "version": 1,
              "materials": { "shared:mat": { "roughness": 0.1, "priority": 5 } },
              "mapping": []
            }
            """);

        var second = Source(
            domain: "b",
            path: "config/vge/material_definitions.json",
            json: """
            {
              "version": 1,
              "materials": { "shared:mat": { "roughness": 0.9, "priority": 5 } },
              "mapping": []
            }
            """);

        // Act: Initialize the registry from these parsed definitions and texture locations.
        PbrMaterialRegistry.Instance.InitializeFromParsedSources(
            logger,
            parsedSources: new[] { first, second },
            textureLocations: Array.Empty<AssetLocation>(),
            strict: true);

        Assert.True(PbrMaterialRegistry.Instance.TryGetMaterial("shared:mat", out var def));
        // Assert: Verify the definition from the later ordered source.
        Assert.Equal(0.9f, def.Roughness, 3);
    }
    #endregion

    #region Texture mapping and overrides
    /// <summary>Texture mapping selects the highest-priority matching rule.</summary>
    [Fact]
    public void MappingRuleOrdering_HigherPriorityWins()
    {
        // Arrange: Define overlapping mapping rules with different priorities for registry resolution.
        var logger = new TestLogger();

        var src = Source(
            domain: "game",
            path: "config/vge/material_definitions.json",
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

        // Act: Initialize the registry from these parsed definitions and texture locations.
        PbrMaterialRegistry.Instance.InitializeFromParsedSources(
            logger,
            parsedSources: new[] { src },
            textureLocations: textures,
            strict: true);

        Assert.True(PbrMaterialRegistry.Instance.TryGetMaterialId(new AssetLocation("game", "textures/block/test.png"), out var materialId));
        // Assert: Verify the material selected by the higher-priority rule.
        Assert.Equal("game:b", materialId);
    }

    /// <summary>Relative texture locations resolve against the texture root before mapping.</summary>
    [Fact]
    public void Mapping_Works_WhenTextureLocationsAreRelativeToTexturesFolder()
    {
        // Arrange: Define a texture path relative to the textures folder for registry resolution.
        // This simulates a real-world AssetManager behavior where GetLocations("textures/")
        // returns paths relative to the base folder (e.g. "block/test.png").
        var logger = new TestLogger();

        var src = Source(
            domain: "game",
            path: "config/vge/material_definitions.json",
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

        // Act: Initialize the registry from these parsed definitions and texture locations.
        PbrMaterialRegistry.Instance.InitializeFromParsedSources(
            logger,
            parsedSources: new[] { src },
            textureLocations: textures,
            strict: true);

        Assert.True(PbrMaterialRegistry.Instance.TryGetMaterialId(new AssetLocation("game", "textures/block/test.png"), out var materialId));
        // Assert: Verify resolution of the registered relative texture path.
        Assert.Equal("game:a", materialId);
    }

    /// <summary>Extensionless registry keys resolve extensionful lookup queries.</summary>
    [Fact]
    public void Mapping_Works_WhenTextureLocationsOmitExtensions_AndLookupAcceptsExtensionfulQueries()
    {
        // Arrange: Define an extensionless texture registration and extensionful query for registry resolution.
        // Some engine/mod call sites use extensionless texture keys ("block/test").
        // Our mapping rules are often file-like ("*.png"), but the registry keyspace is extensionless.
        var logger = new TestLogger();

        var src = Source(
            domain: "game",
            path: "config/vge/material_definitions.json",
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

        var textures = Textures("block/test");

        // Act: Initialize the registry from these parsed definitions and texture locations.
        PbrMaterialRegistry.Instance.InitializeFromParsedSources(
            logger,
            parsedSources: new[] { src },
            textureLocations: textures,
            strict: true);

        Assert.True(PbrMaterialRegistry.Instance.TryGetMaterialId(new AssetLocation("game", "textures/block/test.png"), out var materialId));
        // Assert: Verify extensionful lookup of an extensionless registration.
        Assert.Equal("game:a", materialId);
    }

    /// <summary>Equal-priority mapping rules select the first matching rule.</summary>
    [Fact]
    public void MappingRuleOrdering_Tie_FirstRuleWins()
    {
        // Arrange: Define tied mapping rules in deterministic source order for registry resolution.
        var logger = new TestLogger();

        var src = Source(
            domain: "game",
            path: "config/vge/material_definitions.json",
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

        // Act: Initialize the registry from these parsed definitions and texture locations.
        PbrMaterialRegistry.Instance.InitializeFromParsedSources(
            logger,
            parsedSources: new[] { src },
            textureLocations: textures,
            strict: true);

        Assert.True(PbrMaterialRegistry.Instance.TryGetMaterialId(new AssetLocation("game", "textures/block/test.png"), out var materialId));
        // Assert: Verify the material selected by the first tied rule.
        Assert.Equal("game:a", materialId);
    }

    /// <summary>Mapping override values are parsed and attached to each matching texture.</summary>
    [Fact]
    public void MappingRuleOverrides_AreParsedAndAttachedPerTexture()
    {
        // Arrange: Define per-texture material and scale overrides for registry resolution.
        var logger = new TestLogger();

        var src = Source(
            domain: "game",
            path: "config/vge/material_definitions.json",
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

        // Act: Initialize the registry from these parsed definitions and texture locations.
        PbrMaterialRegistry.Instance.InitializeFromParsedSources(
            logger,
            parsedSources: new[] { src },
            textureLocations: textures,
            strict: true);

        var key = new AssetLocation("game", "textures/block/test.png");
        // Assert: Verify the material and scale overrides attached to this texture.
        Assert.True(PbrMaterialRegistry.Instance.TryGetOverrides(key, out PbrMaterialTextureOverrides overrides));

        Assert.Equal("rule", overrides.RuleId);
        Assert.Equal(new AssetLocation("game", "config/vge/material_definitions.json"), overrides.RuleSource);

        Assert.NotNull(overrides.MaterialParams);
        Assert.Equal("game", overrides.MaterialParams!.Domain);
        Assert.Equal("textures/vge/params/test_params.png", overrides.MaterialParams.Path);

        Assert.NotNull(overrides.NormalHeight);
        Assert.Equal("mymod", overrides.NormalHeight!.Domain);
        Assert.Equal("textures/vge/nh/test_nh.dds", overrides.NormalHeight.Path);
    }

    /// <summary>The first equal-priority rule supplies its material override.</summary>
    [Fact]
    public void MappingRuleOrdering_Tie_FirstRuleWins_AndItsOverridesAreUsed()
    {
        // Arrange: Define tied rules with distinct override values for registry resolution.
        var logger = new TestLogger();

        var src = Source(
            domain: "game",
            path: "config/vge/material_definitions.json",
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

        // Act: Initialize the registry from these parsed definitions and texture locations.
        PbrMaterialRegistry.Instance.InitializeFromParsedSources(
            logger,
            parsedSources: new[] { src },
            textureLocations: textures,
            strict: true);

        var key = new AssetLocation("game", "textures/block/test.png");

        Assert.True(PbrMaterialRegistry.Instance.TryGetMaterialId(key, out var materialId));
        // Assert: Verify the first tied rule and its override values.
        Assert.Equal("game:a", materialId);

        Assert.True(PbrMaterialRegistry.Instance.TryGetOverrides(key, out PbrMaterialTextureOverrides overrides));
        Assert.Equal("first", overrides.RuleId);
        Assert.Equal(new AssetLocation("game", "textures/vge/params/first.png"), overrides.MaterialParams);
    }
    #endregion

    #region Invalid values and identifiers
    /// <summary>Invalid override strings warn and do not replace the mapped value.</summary>
    [Fact]
    public void InvalidOverrideStrings_WarnAndAreTreatedAsNoOverride()
    {
        // Arrange: Define unparseable override strings and a warning collector for registry resolution.
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
            path: "config/vge/material_definitions.json",
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

        // Act: Initialize the registry from these parsed definitions and texture locations.
        PbrMaterialRegistry.Instance.InitializeFromParsedSources(
            logger,
            parsedSources: new[] { src },
            textureLocations: textures,
            strict: true);

        Assert.Empty(PbrMaterialRegistry.Instance.OverridesByTexture);

        // Warning is emitted once per rule/kind (not once per matching texture).
        Assert.Single(warnings);
        // Assert: Verify the missing override and invalid-value warning.
        Assert.Contains("PBR override ignored", warnings[0]);
        Assert.Contains("unsupported extension", warnings[0]);
    }

    /// <summary>A missing scale override retains identity values without warnings.</summary>
    [Fact]
    public void OverridesScale_Missing_DefaultsToIdentity_NoWarnings()
    {
        // Arrange: Define a mapping with no scale override for registry resolution.
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
          path: "config/vge/material_definitions.json",
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

        // Act: Initialize the registry from these parsed definitions and texture locations.
        PbrMaterialRegistry.Instance.InitializeFromParsedSources(
          logger,
          parsedSources: new[] { src },
          textureLocations: textures,
          strict: true);

        var key = new AssetLocation("game", "textures/block/test.png");
        // Assert: Verify identity scale values and absence of warnings.
        Assert.True(PbrMaterialRegistry.Instance.TryGetOverrides(key, out PbrMaterialTextureOverrides overrides));

        Assert.Equal(PbrOverrideScale.Identity, overrides.Scale);
        Assert.Empty(warnings);
    }

    /// <summary>Invalid scale components warn and recover to one.</summary>
    [Fact]
    public void OverridesScale_InvalidValues_WarnAndDefaultToOne()
    {
        // Arrange: Define out-of-range scale components for registry resolution.
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
          path: "config/vge/material_definitions.json",
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

        // Act: Initialize the registry from these parsed definitions and texture locations.
        PbrMaterialRegistry.Instance.InitializeFromParsedSources(
            logger,
            parsedSources: new[] { src },
            textureLocations: textures,
            strict: true);

        var key = new AssetLocation("game", "textures/block/test.png");
        // Assert: Verify fallback scale components and the invalid-value warning.
        Assert.True(PbrMaterialRegistry.Instance.TryGetOverrides(key, out PbrMaterialTextureOverrides overrides));

        Assert.Equal(1f, overrides.Scale.Roughness);
        Assert.Equal(2f, overrides.Scale.Metallic);
        Assert.Equal(0.5f, overrides.Scale.Emissive);

        Assert.Single(warnings);
        Assert.Contains("Invalid override scale value", warnings[0]);
        Assert.Contains("roughness", warnings[0]);
    }

    /// <summary>Nonfinite scale components warn and recover to one.</summary>
    [Fact]
    public void OverridesScale_NaNAndInfinity_WarnAndDefaultToOne()
    {
        // Arrange: Define NaN and infinite scale components for registry resolution.
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
          Location: new AssetLocation("game", "config/vge/material_definitions.json"),
          File: file);

        var textures = Textures("textures/block/test.png");

        // Act: Initialize the registry from these parsed definitions and texture locations.
        PbrMaterialRegistry.Instance.InitializeFromParsedSources(
          logger,
          parsedSources: new[] { src },
          textureLocations: textures,
          strict: true);

        var key = new AssetLocation("game", "textures/block/test.png");
        // Assert: Verify finite fallbacks for nonfinite scale values and their warning.
        Assert.True(PbrMaterialRegistry.Instance.TryGetOverrides(key, out PbrMaterialTextureOverrides overrides));

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

    /// <summary>Mappings naming unknown materials warn and are omitted.</summary>
    [Fact]
    public void MappingRuleReferencesUnknownMaterial_WarnsAndSkipsMapping()
    {
        // Arrange: Define a mapping that names an undefined material for registry resolution.
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
            path: "config/vge/material_definitions.json",
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
        // Act: Initialize the registry from these parsed definitions and texture locations.
        PbrMaterialRegistry.Instance.InitializeFromParsedSources(
            logger,
            parsedSources: new[] { src },
            textureLocations: new[] { texture },
            strict: true);

        Assert.False(PbrMaterialRegistry.Instance.TryGetMaterialId(texture, out _));
        // Assert: Verify the skipped mapping and unknown-material warning.
        Assert.True(sawUnknownMaterialWarning);
    }

    /// <summary>Local material identifiers are qualified and normalized consistently.</summary>
    [Fact]
    public void LocalMaterialId_IsQualifiedAndNormalized()
    {
        // Arrange: Define a mixed-case local material identifier for registry resolution.
        var logger = new TestLogger();

        var src = Source(
            domain: "mymod",
            path: "config/vge/material_definitions.json",
            json: """
            {
              "version": 1,
              "materials": {
                "Stone": { "roughness": 0.42 }
              },
              "mapping": []
            }
            """);

        // Act: Initialize the registry from these parsed definitions and texture locations.
        PbrMaterialRegistry.Instance.InitializeFromParsedSources(
            logger,
            parsedSources: new[] { src },
            textureLocations: Array.Empty<AssetLocation>(),
            strict: true);

        Assert.True(PbrMaterialRegistry.Instance.MaterialById.ContainsKey("mymod:stone"));
        // Assert: Verify the normalized domain-qualified material key.
        Assert.False(PbrMaterialRegistry.Instance.MaterialById.ContainsKey("stone"));

        Assert.True(PbrMaterialRegistry.Instance.TryGetMaterial("MyMod:Stone", out var def));
        Assert.Equal(0.42f, def.Roughness, 3);
    }
    #endregion
    #endregion

    #region Private
    /// <summary>Deserializes one material source with the requested domain and asset path.</summary>
    private static PbrMaterialDefinitionsSource Source(string domain, string path, string json)
    {
        var file = JsonConvert.DeserializeObject<PbrMaterialDefinitionsJsonFile>(json)
            ?? throw new InvalidOperationException("Failed to deserialize json");

        return new PbrMaterialDefinitionsSource(
            Domain: domain,
            Location: new AssetLocation(domain, path),
            File: file);
    }

    /// <summary>Creates game-domain texture locations for mapping scenarios.</summary>
    private static IReadOnlyList<AssetLocation> Textures(params string[] paths)
    {
        return paths.Select(p => new AssetLocation("game", p)).ToArray();
    }
    #endregion
}
