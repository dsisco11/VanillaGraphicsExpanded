using Newtonsoft.Json;
using VanillaGraphicsExpanded.Tests.Support.Cache;
using VanillaGraphicsExpanded.PBR.Materials;
using Vintagestory.API.Common;

namespace VanillaGraphicsExpanded.Tests.Component.PBR.Materials;

/// <summary>Verifies registry admission of shared BRDF authoring data.</summary>
[Collection("PbrMaterialRegistry")]
public sealed class BRDFPropertiesTests
{
    #region Public API
    /// <summary>Flat shared authoring fields preserve defaults, material overrides, and mapping multipliers.</summary>
    [Fact]
    public void SharedAuthoringPropertiesPreserveRegistryBehavior()
    {
        // Arrange: define shared defaults, one material override, and a mapping-level scale multiplier.
        var file = JsonConvert.DeserializeObject<PbrMaterialDefinitionsJsonFile>("""
            {"version":1,"defaults":{"roughness":0.8,"metallic":0.2,"emissive":3,
              "noise":{"roughness":0.1,"metallic":0.2},"scale":{"normal":2,"depth":3}},
             "materials":{"stone":{"roughness":0,"noise":{"roughness":0},"notes":"test","priority":4}},
             "mapping":[{"match":{"glob":"assets/game/textures/block/test.png"},
              "values":{"material":"stone","overrides":{"scale":{"normal":4,"depth":5}}}}]}
            """)!;
        var texture = new AssetLocation("game", "textures/block/test.png");

        // Act: serialize the parsed material to inspect its public authoring representation.
        var serialized = Newtonsoft.Json.Linq.JObject.Parse(JsonConvert.SerializeObject(file.Materials!["stone"]));

        // Assert: parsed models and serialized fields retain the flat shared BRDF contract.
        Assert.IsAssignableFrom<BRDFPropertiesJson>(file.Defaults);
        Assert.IsAssignableFrom<BRDFPropertiesJson>(file.Materials["stone"]);
        Assert.NotNull(serialized["roughness"]);
        Assert.Null(serialized["properties"]);
        Assert.Null(serialized["faces"]);

        try
        {
            // Act: admit the parsed source and resolve its material and mapped texture.
            PbrMaterialRegistry.Instance.InitializeFromParsedSources(new TestLogger(),
                [new("game", new AssetLocation("game", "config/vge/material_definitions.json"), file)], [texture], true);

            // Assert: defaults, overrides, material metadata, and multiplied scales survive registry resolution.
            Assert.True(PbrMaterialRegistry.Instance.TryGetMaterial("game:stone", out var material));
            Assert.Equal(new BRDFProperties(0, 0.2f, 3, new(0, 0.2f, 0, 0, 0), new(1, 1, 1, 2, 3)), material.Properties);
            Assert.Equal("test", material.Notes);
            Assert.Equal(4, material.Priority);
            Assert.True(PbrMaterialRegistry.Instance.TryGetScale(texture, out var mapped));
            Assert.Equal(8, mapped.Normal);
            Assert.Equal(15, mapped.Depth);
        }
        finally
        {
            PbrMaterialRegistry.Instance.Clear();
        }
    }
    #endregion
}





