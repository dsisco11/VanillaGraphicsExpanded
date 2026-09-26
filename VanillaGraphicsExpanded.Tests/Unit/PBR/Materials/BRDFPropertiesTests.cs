using System.Numerics;
using Newtonsoft.Json;
using VanillaGraphicsExpanded.PBR.Materials;
using VanillaGraphicsExpanded.Tests.Fixtures;
using Vintagestory.API.Common;

namespace VanillaGraphicsExpanded.Tests.Unit.PBR.Materials;

/// <summary>Verifies shared BRDF encapsulation preserves existing material authoring and lighting behavior.</summary>
[Collection("PbrMaterialRegistry")]
public sealed class BRDFPropertiesTests
{
    #region Surface construction
    /// <summary>Dielectric and metallic materials keep the existing texture-derived diffuse/Fresnel split.</summary>
    [Theory]
    [InlineData(0f)]
    [InlineData(1f)]
    public void SurfaceBuilderPreservesDiffuseAndFresnelConvention(float metallic)
    {
        var properties = new BRDFProperties(0.2f, metallic, 0.5f, default, PbrOverrideScale.Identity);
        var color = new Vector3(0.8f, 0.4f, 0.2f);
        var surface = PbrMaterialSurfaceBuilder.Build(properties, color);
        Assert.Equal(metallic == 0 ? color : Vector3.Zero, surface.DiffuseAlbedo);
        Assert.Equal(metallic == 0 ? new Vector3(0.04f) : color, surface.SpecularF0);
        Assert.Equal(0.2f, surface.Roughness);
        Assert.Equal(metallic, surface.Metallic);
        Assert.Equal(0.5f, surface.Emissive);
    }

    /// <summary>Zero multipliers suppress channels and representative surface clamps remain unchanged.</summary>
    [Fact]
    public void SurfaceBuilderPreservesScaleAndClamping()
    {
        var properties = new BRDFProperties(0.8f, 0.6f, 12, default, new(2, 0, 0, 1, 1));
        var actual = PbrMaterialSurfaceBuilder.Build(properties, new Vector3(-1, 0.5f, 2));
        Assert.Equal(new PbrMaterialSurface(1, 0, 0, new Vector3(0, 0.5f, 1), new Vector3(0.04f)), actual);
        var hdr = PbrMaterialSurfaceBuilder.Build(properties with { Scale = PbrOverrideScale.Identity }, Vector3.One);
        Assert.Equal(1, hdr.Emissive);
        Assert.Equal(0.4f, hdr.DiffuseAlbedo.X, 6);
        Assert.Equal(0.4f, hdr.DiffuseAlbedo.Y, 6);
        Assert.Equal(0.4f, hdr.DiffuseAlbedo.Z, 6);
        Assert.InRange(hdr.SpecularF0.X, 0.61599f, 0.61601f);
    }
    #endregion

    #region Registry compatibility
    /// <summary>Shared JSON fields remain flat and preserve defaults, material overrides and mapping multipliers.</summary>
    [Fact]
    public void SharedAuthoringPropertiesPreserveRegistryBehavior()
    {
        var file = JsonConvert.DeserializeObject<PbrMaterialDefinitionsJsonFile>("""
            {"version":1,"defaults":{"roughness":0.8,"metallic":0.2,"emissive":3,
              "noise":{"roughness":0.1,"metallic":0.2},"scale":{"normal":2,"depth":3}},
             "materials":{"stone":{"roughness":0,"noise":{"roughness":0},"notes":"test","priority":4}},
             "mapping":[{"match":{"glob":"assets/game/textures/block/test.png"},
              "values":{"material":"stone","overrides":{"scale":{"normal":4,"depth":5}}}}]}
            """)!;
        Assert.IsAssignableFrom<BRDFPropertiesJson>(file.Defaults);
        Assert.IsAssignableFrom<BRDFPropertiesJson>(file.Materials!["stone"]);
        var serialized = Newtonsoft.Json.Linq.JObject.Parse(JsonConvert.SerializeObject(file.Materials["stone"]));
        Assert.NotNull(serialized["roughness"]);
        Assert.Null(serialized["properties"]);
        Assert.Null(serialized["faces"]);
        var texture = new AssetLocation("game", "textures/block/test.png");
        try
        {
            PbrMaterialRegistry.Instance.InitializeFromParsedSources(new TestLogger(),
                [new("game", new AssetLocation("game", "config/vge/material_definitions.json"), file)], [texture], true);
            Assert.True(PbrMaterialRegistry.Instance.TryGetMaterial("game:stone", out var material));
            Assert.Equal(new BRDFProperties(0, 0.2f, 3, new(0, 0.2f, 0, 0, 0), new(1, 1, 1, 2, 3)), material.Properties);
            Assert.Equal("test", material.Notes);
            Assert.Equal(4, material.Priority);
            Assert.True(PbrMaterialRegistry.Instance.TryGetScale(texture, out var mapped));
            Assert.Equal(8, mapped.Normal);
            Assert.Equal(15, mapped.Depth);
        }
        finally { PbrMaterialRegistry.Instance.Clear(); }
    }
    #endregion
}
