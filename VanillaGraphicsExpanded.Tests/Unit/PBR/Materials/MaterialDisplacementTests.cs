using VanillaGraphicsExpanded.PBR.Tessellation;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using VanillaGraphicsExpanded.PBR.Materials;
using VanillaGraphicsExpanded.Tests.Fixtures;
using Vintagestory.API.Common;

namespace VanillaGraphicsExpanded.Tests.Unit.PBR.Materials;

/// <summary>Protects opt-in physical displacement independently of BRDF authoring.</summary>
[Collection("PbrMaterialRegistry")]
public sealed class MaterialDisplacementTests
{
    #region Authoring validation
    /// <summary>Valid amplitudes retain metre units and invalid values opt out with a diagnostic.</summary>
    [Theory]
    [InlineData(0f, 0f, false)]
    [InlineData(.02f, .02f, false)]
    [InlineData(.05f, .05f, false)]
    [InlineData(-.01f, 0f, true)]
    [InlineData(.051f, 0f, true)]
    [InlineData(float.NaN, 0f, true)]
    [InlineData(float.PositiveInfinity, 0f, true)]
    public void AmplitudesAreBoundedOptIn(float input, float expected, bool invalid)
    {
        var diagnostics=new List<string>();
        Assert.Equal(expected, MaterialDisplacement.ResolveAmplitude(input, diagnostics.Add));
        Assert.Equal(invalid ? 1 : 0, diagnostics.Count);
    }

    /// <summary>Definition parsing resolves material amplitude while missing values remain disabled.</summary>
    [Fact]
    public void RegistryResolvesMaterialOnlyAmplitude()
    {
        var file=JsonConvert.DeserializeObject<PbrMaterialDefinitionsJsonFile>("""
            {"version":1,"defaults":{"roughness":0.8},"materials":{
                "plain":{},"relief":{"displacement":{"amplitudeMetres":0.03}},
                "invalid":{"displacement":{"amplitudeMetres":1}}},"mapping":[]}
            """)!;
        try
        {
            PbrMaterialRegistry.Instance.InitializeFromParsedSources(new TestLogger(),
                [new("game",new AssetLocation("game","config/vge/material_definitions.json"),file)],[],true);
            Assert.True(PbrMaterialRegistry.Instance.TryGetMaterial("game:plain",out var plain));
            Assert.Equal(0,plain.DisplacementAmplitudeMetres);
            Assert.True(PbrMaterialRegistry.Instance.TryGetMaterial("game:relief",out var relief));
            Assert.Equal(.03f,relief.DisplacementAmplitudeMetres);
            Assert.True(PbrMaterialRegistry.Instance.TryGetMaterial("game:invalid",out var invalid));
            Assert.Equal(0,invalid.DisplacementAmplitudeMetres);
        }
        finally { PbrMaterialRegistry.Instance.Clear(); }
    }
    /// <summary>The shipped schema exposes geometric amplitude only on material definitions.</summary>
    [Fact]
    public void SchemaKeepsDisplacementOutsideBrdfAndMappingOverrides()
    {
        var schema=JObject.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory,"schemas/pbr_material_definitions.schema.json")));
        var definitions=schema["$defs"]!;
        Assert.NotNull(definitions["material"]!["properties"]!["displacement"]);
        Assert.Null(definitions["BRDFProperties"]!["properties"]!["displacement"]);
        Assert.Null(definitions["mappingOverrides"]!["properties"]!["displacement"]);
        Assert.Equal(0f,(float)definitions["displacement"]!["properties"]!["amplitudeMetres"]!["minimum"]!);
        Assert.Equal(.05f,(float)definitions["displacement"]!["properties"]!["amplitudeMetres"]!["maximum"]!);
    }

    /// <summary>Invalid subdivision settings become finite ordered bounds before GPU publication.</summary>
    [Fact]
    public void SubdivisionSettingsSanitizeNonFiniteAndOutOfRangeValues()
    {
        var settings=new VanillaGraphicsExpanded.PBR.Tessellation.TerrainSubdivisionSettings
        {MaximumLevel=99,TargetEdgePixels=float.NaN,FadeStartMetres=float.PositiveInfinity,FadeEndMetres=-1};
        settings.Sanitize();
        Assert.Equal(8,settings.MaximumLevel); Assert.Equal(16,settings.TargetEdgePixels);
        Assert.Equal(8,settings.FadeStartMetres); Assert.Equal(9,settings.FadeEndMetres);
        settings.MaximumLevel=0; settings.TargetEdgePixels=999; settings.FadeStartMetres=200; settings.FadeEndMetres=float.NaN;
        settings.Sanitize();
        Assert.Equal(1,settings.MaximumLevel); Assert.Equal(256,settings.TargetEdgePixels);
        Assert.Equal(127,settings.FadeStartMetres); Assert.Equal(128,settings.FadeEndMetres);
    }
    #endregion
}

