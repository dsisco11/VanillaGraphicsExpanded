using Moq;
using VanillaGraphicsExpanded.Tests.Support.Cache;
using Newtonsoft.Json;
using VanillaGraphicsExpanded.PBR.Materials;

using Vintagestory.API.Client;
using Vintagestory.API.Common;

using Xunit;

namespace VanillaGraphicsExpanded.Tests.Component.PBR.Materials;

/// <summary>Checks that the material registry resolves surface queries across texture key variants.</summary>
[Collection("PbrMaterialRegistry")]
public sealed class PbrMaterialRegistryTextureNormalizationTests
{
    #region Public API
    /// <summary>Normalizes omitted texture prefixes and file extensions through the registry lookup path.</summary>
    [Fact]
    public void TryGetSurface_NormalizesTexturesPrefixAndExtension()
    {
        // Arrange: map one material to a concrete texture using the registry's parsed-source boundary.
        PbrMaterialRegistry registry = PbrMaterialRegistry.Instance;
        registry.Clear();

        try
        {
            var file = JsonConvert.DeserializeObject<PbrMaterialDefinitionsJsonFile>("""
                {"version":1,"materials":{"stone":{"roughness":0.25}},"mapping":[
                  {"match":{"glob":"assets/game/textures/block/test.png"},"values":{"material":"stone"}}]}
                """)!;
            var source = new PbrMaterialDefinitionsSource(
                "game",
                new AssetLocation("game", "config/vge/material_definitions.json"),
                file);
            var texture = new AssetLocation("game", "textures/block/test.png");
            registry.InitializeFromParsedSources(new TestLogger(), [source], [texture], strict: true);

            // A missing asset is sufficient here: the registry still publishes the material surface,
            // while texture decoding is unrelated to key normalization.
            var assets = new Mock<IAssetManager>();
            assets.Setup(manager => manager.TryGet(It.IsAny<AssetLocation>(), loadAsset: true))
                .Returns((IAsset)null!);
            var api = new Mock<ICoreClientAPI>();
            api.SetupGet(client => client.Assets).Returns(assets.Object);
            api.SetupGet(client => client.Logger).Returns(new TestLogger());

            // Act: build the derived surface through the public registry path.
            registry.BuildDerivedSurfaces(api.Object);

            // Assert: registry construction publishes the authored canonical key, and all supported
            // query forms resolve the same built surface through TryGetSurface.
            Assert.Equal(new AssetLocation("game", "textures/block/test"), Assert.Single(registry.SurfaceByTexture.Keys));
            AssetLocation[] queryForms =
            [
                new("game", "block/test"),
                new("game", "textures/block/test"),
                new("game", "Textures/Block/Test.PNG"),
                new("game", "block/test.DDS"),
            ];
            Assert.True(registry.TryGetSurface(queryForms[0], out PbrMaterialSurface expected));
            Assert.Equal(0.25f, expected.Roughness);
            foreach (AssetLocation query in queryForms)
            {
                Assert.True(registry.TryGetSurface(query, out PbrMaterialSurface actual));
                Assert.Equal(expected, actual);
            }
        }
        finally
        {
            registry.Clear();
        }
    }
    #endregion
}
