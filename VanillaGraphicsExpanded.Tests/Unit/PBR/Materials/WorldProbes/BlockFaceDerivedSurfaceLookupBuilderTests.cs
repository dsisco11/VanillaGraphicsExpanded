using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using VanillaGraphicsExpanded.PBR.Materials;
using VanillaGraphicsExpanded.PBR.Materials.Energy;
using VanillaGraphicsExpanded.PBR.Materials.WorldProbes;
using Vintagestory.API.Client;
using Vintagestory.API.Common;

namespace VanillaGraphicsExpanded.Tests.Unit.PBR.Materials.WorldProbes;

/// <summary>Verifies face lookup derivation from authored surfaces and missing-surface fallback behavior.</summary>
public sealed class BlockFaceDerivedSurfaceLookupBuilderTests
{
    #region Public API
    /// <summary>Repeated lookup building preserves metadata and projects authored material values onto every face.</summary>
    [Fact]
    public void Build_IsDeterministicForSameInputs()
    {
        // Arrange: map a single block texture to an authored surface with independent source values.
        var block = new Block
        {
            BlockId = 3,
            Code = new AssetLocation("game", "testblock"),
            Textures = new Dictionary<string, CompositeTexture>
            {
                ["all"] = new CompositeTexture(new AssetLocation("game", "block/test/albedo"))
            }
        };
        IList<Block> blocks = new List<Block> { block };
        var texKey = new AssetLocation("game", "textures/block/test/albedo");
        var surfaceByTexture = new Dictionary<AssetLocation, PbrMaterialSurface>
        {
            [texKey] = new PbrMaterialSurface(0.5f, 0f, 0f,
                new Vector3(0.2f, 0.3f, 0.4f), new Vector3(0.04f))
        };

        // Act: build the lookup twice using the same source dictionary.
        DerivedSurface[] first = BlockFaceDerivedSurfaceLookupBuilder.Build(blocks, surfaceByTexture, out var statsA);
        DerivedSurface[] second = BlockFaceDerivedSurfaceLookupBuilder.Build(blocks, surfaceByTexture, out var statsB);

        // Assert: lookup metadata, per-face coefficients, and the dictionary-owned source value remain intact.
        Assert.True(first.SequenceEqual(second));
        Assert.Equal(statsA, statsB);
        Assert.Equal(3, statsA.MaxBlockId);
        Assert.Equal((3 + 1) * 6, statsA.TotalFaces);
        Assert.True(statsA.ResolvedFaces > 0);
        PbrMaterialSurface authored = surfaceByTexture[texKey];
        Vector3 expectedDiffuse = MicrofacetEnergyTable.Shared.DiffuseAlbedo(authored);
        for (int face = 0; face < 6; face++)
        {
            Assert.Equal(expectedDiffuse, first[3 * 6 + face].DiffuseAlbedo);
            Assert.Equal(authored.SpecularF0, first[3 * 6 + face].SpecularF0);
        }
        Assert.Equal(new Vector3(0.2f, 0.3f, 0.4f), authored.DiffuseAlbedo);
        Assert.Equal(0, authored.Emissive);
        foreach (DerivedSurface surface in first)
        {
            Assert.InRange(surface.DiffuseAlbedo.X, 0f, 1f);
            Assert.InRange(surface.DiffuseAlbedo.Y, 0f, 1f);
            Assert.InRange(surface.DiffuseAlbedo.Z, 0f, 1f);
            Assert.InRange(surface.SpecularF0.X, 0f, 1f);
            Assert.InRange(surface.SpecularF0.Y, 0f, 1f);
            Assert.InRange(surface.SpecularF0.Z, 0f, 1f);
        }
    }
    /// <summary>Missing material lookup keeps the diagnostic magenta fallback after interface reflection is reserved.</summary>
    [Fact]
    public void MissingSurfaceUsesCompensatedMagentaFallback()
    {
        // Arrange: provide a textured block but no authored surface for its texture.
        var block = new Block
        {
            BlockId = 3,
            Code = new AssetLocation("game", "missing"),
            Textures = new Dictionary<string, CompositeTexture>
            {
                ["all"] = new CompositeTexture(new AssetLocation("game", "block/missing"))
            }
        };
        Vector3 expectedDiffuse = MicrofacetEnergyTable.Shared.DiffuseAlbedo(
            new(PbrMaterialSurface.Default.Roughness, 0, 0, new(1, 0, 1), new(0.04f)));

        // Act: derive the face lookup with an empty authored surface table.
        DerivedSurface[] result = BlockFaceDerivedSurfaceLookupBuilder.Build(
            [block], new Dictionary<AssetLocation, PbrMaterialSurface>(), out var stats);

        // Assert: every face uses default diagnostic payload and compensated magenta diffuse energy.
        Assert.True(stats.DefaultsUsed > 0);
        for (int face = 0; face < 6; face++)
        {
            Assert.Equal(DerivedSurface.Default, result[18 + face]);
            Assert.Equal(expectedDiffuse, result[18 + face].DiffuseAlbedo);
            Assert.Equal(new Vector3(0.04f), result[18 + face].SpecularF0);
        }
        Assert.InRange(expectedDiffuse.X, 0.0001f, 0.9999f);
        Assert.Equal(0, expectedDiffuse.Y);
        Assert.Equal(expectedDiffuse.X, expectedDiffuse.Z);
    }
    #endregion
}
