using Newtonsoft.Json;
using VanillaGraphicsExpanded.PBR.Materials;
using VanillaGraphicsExpanded.Tests.Fixtures;
using Vintagestory.API.Common;

namespace VanillaGraphicsExpanded.Tests;

/// <summary>Checks opt-in transmission inheritance and independent RGBA publication of cached RGB data.</summary>
[Collection("PbrMaterialRegistry")]
public sealed class MaterialTransmissionTests
{
    #region Material schema
    /// <summary>Materials inherit transmission while explicit zero disables it and out-of-range values are bounded.</summary>
    [Fact]
    public void RegistryResolvesTransmissionDefaultsAndExplicitZero()
    {
        var file = JsonConvert.DeserializeObject<PbrMaterialDefinitionsJsonFile>("""
            {"version":1,"defaults":{"transmission":0.4},
             "materials":{"leaf":{},"opaque":{"transmission":0},"strong":{"transmission":2}}}
            """)!;
        try
        {
            PbrMaterialRegistry.Instance.InitializeFromParsedSources(new TestLogger(),
                [new("game", new AssetLocation("game", "config/vge/material_definitions.json"), file)], [], true);
            Assert.True(PbrMaterialRegistry.Instance.TryGetMaterial("game:leaf", out var leaf));
            Assert.Equal(0.4f, leaf.Transmission);
            Assert.Equal(0.4f, leaf.Properties.Transmission);
            Assert.True(PbrMaterialRegistry.Instance.TryGetMaterial("game:opaque", out var opaque));
            Assert.Equal(0, opaque.Transmission);
            Assert.True(PbrMaterialRegistry.Instance.TryGetMaterial("game:strong", out var strong));
            Assert.Equal(1, strong.Transmission);
        }
        finally { PbrMaterialRegistry.Instance.Clear(); }
    }
    #endregion

    #region GPU packing
    /// <summary>RGB cached values are unchanged and alpha comes from current material settings, never an override mask.</summary>
    [Theory]
    [InlineData(0.35f, 0.35f)]
    [InlineData(0f, 0f)]
    [InlineData(-1f, 0f)]
    [InlineData(2f, 1f)]
    [InlineData(float.NaN, 0f)]
    public void PacksCurrentTransmissionWithoutMutatingCachedRgb(float input, float expected)
    {
        float[] rgb = [0.2f, 0.3f, 0.4f, 0.5f, 0.6f, 0.7f];
        Assert.Equal(new float[] { .2f, .3f, .4f, expected, .5f, .6f, .7f, expected }, MaterialTransmission.Pack(rgb, input));
        Assert.Equal(new float[] { .2f, .3f, .4f, .5f, .6f, .7f }, rgb);
    }
    #endregion
}
