using Newtonsoft.Json;
using VanillaGraphicsExpanded.Tests.Support.Cache;

using VanillaGraphicsExpanded.PBR.Materials;

using Vintagestory.API.Common;
using Xunit;

namespace VanillaGraphicsExpanded.Tests.Component.PBR.Materials;

/// <summary>Checks material-only displacement values during parsed-source registry admission.</summary>
[Collection("PbrMaterialRegistry")]
public sealed class MaterialDisplacementTests
{
    #region Public API
    /// <summary>Definition parsing resolves per-material amplitude and leaves absent or invalid values disabled.</summary>
    [Fact]
    public void RegistryResolvesMaterialOnlyAmplitude()
    {
        // Arrange: define materials with absent, valid, and rejected displacement amplitudes.
        var file = JsonConvert.DeserializeObject<PbrMaterialDefinitionsJsonFile>("""
            {"version":1,"defaults":{"roughness":0.8},"materials":{
                "plain":{},"relief":{"displacement":{"amplitudeMetres":0.03}},
                "invalid":{"displacement":{"amplitudeMetres":1}}},"mapping":[]}
            """)!;

        try
        {
            // Act: admit the authored definitions through the registry's parsed-source boundary.
            PbrMaterialRegistry.Instance.InitializeFromParsedSources(new TestLogger(),
                [new("game", new AssetLocation("game", "config/vge/material_definitions.json"), file)], [], true);

            // Assert: missing and invalid amplitudes stay disabled, while the valid value is retained.
            Assert.True(PbrMaterialRegistry.Instance.TryGetMaterial("game:plain", out var plain));
            Assert.Equal(0, plain.DisplacementAmplitudeMetres);
            Assert.True(PbrMaterialRegistry.Instance.TryGetMaterial("game:relief", out var relief));
            Assert.Equal(.03f, relief.DisplacementAmplitudeMetres);
            Assert.True(PbrMaterialRegistry.Instance.TryGetMaterial("game:invalid", out var invalid));
            Assert.Equal(0, invalid.DisplacementAmplitudeMetres);
        }
        finally
        {
            PbrMaterialRegistry.Instance.Clear();
        }
    }
    #endregion
}
