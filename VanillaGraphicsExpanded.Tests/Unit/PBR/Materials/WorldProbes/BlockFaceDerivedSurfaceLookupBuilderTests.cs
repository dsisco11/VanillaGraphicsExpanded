using System.Collections.Generic;
using System.Linq;
using System.Numerics;

using VanillaGraphicsExpanded.PBR.Materials;
using VanillaGraphicsExpanded.PBR.Materials.WorldProbes;

using Vintagestory.API.Client;
using Vintagestory.API.Common;

using Xunit;

namespace VanillaGraphicsExpanded.Tests.Unit.PBR.Materials.WorldProbes;

public sealed class BlockFaceDerivedSurfaceLookupBuilderTests
{
    [Fact]
    public void Build_IsDeterministicForSameInputs()
    {
        var block = new Block
        {
            BlockId = 3,
            Code = new AssetLocation("game", "testblock"),
            Textures = new Dictionary<string, CompositeTexture>
            {
                ["all"] = new CompositeTexture(new AssetLocation("game", "block/test/albedo")),
            }
        };

        IList<Block> blocks = new List<Block> { block };

        var texKey = new AssetLocation("game", "textures/block/test/albedo.png");
        var surfaceByTexture = new Dictionary<AssetLocation, PbrMaterialSurface>
        {
            [texKey] = new PbrMaterialSurface(
                Roughness: 0.5f,
                Metallic: 0f,
                Emissive: 0f,
                DiffuseAlbedo: new Vector3(0.2f, 0.3f, 0.4f),
                SpecularF0: new Vector3(0.04f))
        };

        DerivedSurface[] a = BlockFaceDerivedSurfaceLookupBuilder.Build(blocks, surfaceByTexture, out var statsA);
        DerivedSurface[] b = BlockFaceDerivedSurfaceLookupBuilder.Build(blocks, surfaceByTexture, out var statsB);

        Assert.True(a.SequenceEqual(b));
        Assert.Equal(statsA, statsB);
        Assert.Equal(3, statsA.MaxBlockId);
        Assert.Equal((3 + 1) * 6, statsA.TotalFaces);
        Assert.True(statsA.ResolvedFaces > 0);

        // Sanity: all derived terms should stay in [0,1] for this setup.
        foreach (DerivedSurface ds in a)
        {
            Assert.InRange(ds.DiffuseAlbedo.X, 0f, 1f);
            Assert.InRange(ds.DiffuseAlbedo.Y, 0f, 1f);
            Assert.InRange(ds.DiffuseAlbedo.Z, 0f, 1f);
            Assert.InRange(ds.SpecularF0.X, 0f, 1f);
            Assert.InRange(ds.SpecularF0.Y, 0f, 1f);
            Assert.InRange(ds.SpecularF0.Z, 0f, 1f);
        }
    }
}
