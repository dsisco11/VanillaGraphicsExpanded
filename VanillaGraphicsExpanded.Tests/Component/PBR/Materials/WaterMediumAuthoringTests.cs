using System.Numerics;
using VanillaGraphicsExpanded.Tests.Support.Cache;
using Newtonsoft.Json;
using VanillaGraphicsExpanded.PBR.Materials;
using Vintagestory.API.Common;

namespace VanillaGraphicsExpanded.Tests.Component.PBR.Materials;

/// <summary>Verifies water-medium inheritance through parsed material registry admission.</summary>
[Collection("PbrMaterialRegistry")]
public sealed class WaterMediumAuthoringTests
{
    #region Public API
    /// <summary>Registry resolution inherits water-medium values without changing unrelated BRDF channels.</summary>
    [Fact]
    public void RegistryResolvesMediumWithoutChangingBrdfChannels()
    {
        // Arrange: define water defaults, an explicit water zero override, and an ordinary material.
        var file = JsonConvert.DeserializeObject<PbrMaterialDefinitionsJsonFile>("""
            {"version":1,"defaults":{"roughness":0.8,"waterMedium":{"density":2,"anisotropy":0.7,
             "scatteringPerMetre":[0.1,0.2,0.3]}},
             "materials":{"water":{"transmission":1,"waterMedium":{"density":0}},"stone":{}},"mapping":[]}
            """)!;
        try
        {
            // Act: initialize the real registry from the parsed source.
            PbrMaterialRegistry.Instance.InitializeFromParsedSources(new TestLogger(),
                [new("game", new AssetLocation("game", "config/vge/material_definitions.json"), file)], [], true);

            // Assert: water inherits omitted medium values; non-water keeps ordinary material defaults.
            Assert.True(PbrMaterialRegistry.Instance.TryGetMaterial("game:water", out var water));
            Assert.True(PbrMaterialRegistry.Instance.TryGetMaterial("game:stone", out var stone));
            Assert.Equal(0, water.WaterMedium!.Value.Density);
            Assert.Equal(.7f, water.WaterMedium.Value.Anisotropy);
            Assert.Equal(new Vector3(.1f, .2f, .3f), water.WaterMedium.Value.ScatteringPerMetre);
            Assert.Equal(2, stone.WaterMedium!.Value.Density);
            Assert.Equal(0, stone.Transmission);
            Assert.Equal(.8f, water.Roughness);
        }
        finally
        {
            PbrMaterialRegistry.Instance.Clear();
        }
    }
    #endregion
}

