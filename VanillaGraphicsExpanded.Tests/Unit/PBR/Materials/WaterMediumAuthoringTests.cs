using System.Numerics;
using Newtonsoft.Json;
using VanillaGraphicsExpanded.PBR.Materials;
using VanillaGraphicsExpanded.Tests.Fixtures;
using Vintagestory.API.Common;

namespace VanillaGraphicsExpanded.Tests.Unit.PBR.Materials;

/// <summary>Checks SI medium authoring, default inheritance and invalid-value handling through the material owner.</summary>
[Collection("PbrMaterialRegistry")]
public sealed class WaterMediumAuthoringTests
{
    #region Public API
    /// <summary>Separates measured pure-water absorption from configurable density and water-type coefficients.</summary>
    [Fact]
    public void MembersInheritAndExplicitZeroSurvives()
    {
        var inherited = new WaterMedium(new(.1f, .2f, .3f), new(.4f), 2, .6f);
        var json = JsonConvert.DeserializeObject<WaterMediumJson>("""
            {"density":0,"anisotropy":0,"scatteringPerMetre":[0,0,0]}
            """)!;
        var actual = WaterMedium.Resolve(json, inherited);
        Assert.Equal(inherited.AdditionalAbsorptionPerMetre, actual.AdditionalAbsorptionPerMetre);
        Assert.Equal(Vector3.Zero, actual.ScatteringPerMetre);
        Assert.Equal(Vector3.Zero, actual.AbsorptionPerMetre);
        Assert.Equal(0, actual.Anisotropy);
        Assert.Equal(new Vector3(.340f, .0565f, .00922f), WaterMedium.Clear.AbsorptionPerMetre);
    }

    /// <summary>Rejects NaN, infinity, negative or malformed coefficients rather than publishing invalid GPU records.</summary>
    [Fact]
    public void InvalidMembersInheritWithDiagnostics()
    {
        var messages = new List<string>();
        var actual = WaterMedium.Resolve(new WaterMediumJson {
            AdditionalAbsorptionPerMetre = [float.NaN, 0, 0], ScatteringPerMetre = [-1, 0],
            Density = float.PositiveInfinity, Anisotropy = 1 }, WaterMedium.Clear, messages.Add);
        Assert.Equal(WaterMedium.Clear, actual);
        Assert.Equal(4, messages.Count);
    }

    /// <summary>Material-specific values inherit defaults through normal parsed-file registry resolution.</summary>
    [Fact]
    public void RegistryResolvesMediumWithoutChangingBrdfChannels()
    {
        var file = JsonConvert.DeserializeObject<PbrMaterialDefinitionsJsonFile>("""
            {"version":1,"defaults":{"roughness":0.8,"waterMedium":{"density":2,"anisotropy":0.7,
             "scatteringPerMetre":[0.1,0.2,0.3]}},
             "materials":{"water":{"transmission":1,"waterMedium":{"density":0}},"stone":{}},"mapping":[]}
            """)!;
        try
        {
            PbrMaterialRegistry.Instance.InitializeFromParsedSources(new TestLogger(),
                [new("game", new AssetLocation("game", "config/vge/material_definitions.json"), file)], [], true);
            Assert.True(PbrMaterialRegistry.Instance.TryGetMaterial("game:water", out var water));
            Assert.True(PbrMaterialRegistry.Instance.TryGetMaterial("game:stone", out var stone));
            Assert.Equal(0, water.WaterMedium!.Value.Density);
            Assert.Equal(.7f, water.WaterMedium.Value.Anisotropy);
            Assert.Equal(new Vector3(.1f, .2f, .3f), water.WaterMedium.Value.ScatteringPerMetre);
            Assert.Equal(2, stone.WaterMedium!.Value.Density);
            Assert.Equal(0, stone.Transmission);
            Assert.Equal(.8f, water.Roughness);
        }
        finally { PbrMaterialRegistry.Instance.Clear(); }
    }
    #endregion
}
