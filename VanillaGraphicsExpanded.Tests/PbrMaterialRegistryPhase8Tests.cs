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
