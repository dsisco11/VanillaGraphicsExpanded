using Newtonsoft.Json;
using VanillaGraphicsExpanded.Tests.Support.Cache;
using VanillaGraphicsExpanded.PBR.Materials;
using Vintagestory.API.Common;

namespace VanillaGraphicsExpanded.Tests.Component.PBR.Materials;

/// <summary>Verifies transmission defaults as they are admitted into the material registry.</summary>
[Collection("PbrMaterialRegistry")]
public sealed class MaterialTransmissionTests
{
    #region Public API
    /// <summary>Materials inherit transmission, explicit zero disables it, and excessive values are bounded.</summary>
    [Fact]
    public void RegistryResolvesTransmissionDefaultsAndExplicitZero()
    {
        // Arrange: define inherited, explicitly disabled, and out-of-range transmission materials.
        var file = JsonConvert.DeserializeObject<PbrMaterialDefinitionsJsonFile>("""
            {"version":1,"defaults":{"transmission":0.4},
             "materials":{"leaf":{},"opaque":{"transmission":0},"strong":{"transmission":2}}}
            """)!;

        try
        {
            // Act: admit the definitions through the registry's parsed-source boundary.
            PbrMaterialRegistry.Instance.InitializeFromParsedSources(new TestLogger(),
                [new("game", new AssetLocation("game", "config/vge/material_definitions.json"), file)], [], true);

            // Assert: inherited and explicit values survive admission while the upper bound is enforced.
            Assert.True(PbrMaterialRegistry.Instance.TryGetMaterial("game:leaf", out var leaf));
            Assert.Equal(0.4f, leaf.Transmission);
            Assert.Equal(0.4f, leaf.Properties.Transmission);
            Assert.True(PbrMaterialRegistry.Instance.TryGetMaterial("game:opaque", out var opaque));
            Assert.Equal(0, opaque.Transmission);
            Assert.True(PbrMaterialRegistry.Instance.TryGetMaterial("game:strong", out var strong));
            Assert.Equal(1, strong.Transmission);
        }
        finally
        {
            PbrMaterialRegistry.Instance.Clear();
        }
    }
    #endregion
}
