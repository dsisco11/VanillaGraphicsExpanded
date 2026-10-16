using System.Collections.Generic;
using System.Linq;
using System.Numerics;

using VanillaGraphicsExpanded.PBR.Materials;
using VanillaGraphicsExpanded.PBR.Materials.WorldProbes;
using VanillaGraphicsExpanded.PBR.Materials.Energy;

using Vintagestory.API.Client;
using Vintagestory.API.Common;

using Xunit;

namespace VanillaGraphicsExpanded.Tests.Unit.PBR.Materials.WorldProbes;

/// <summary>Checks derived diffuse transport budgets while retaining authored source materials and fallback diagnostics.</summary>
public sealed class BlockFaceDerivedSurfaceLookupBuilderTests
{
    #region Public API
    /// <summary>Repeated lookup construction preserves deterministic metadata and resolved projected-average material coefficients.</summary>
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

        var texKey = new AssetLocation("game", "textures/block/test/albedo");
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
        PbrMaterialSurface authored=surfaceByTexture[texKey];
        Vector3 expected=MicrofacetEnergyTable.Shared.DiffuseAlbedo(authored);
        for(int face=0;face<6;face++)
        {
            Assert.Equal(expected,a[3*6+face].DiffuseAlbedo);
            Assert.Equal(authored.SpecularF0,a[3*6+face].SpecularF0);
        }
        Assert.Equal(new Vector3(.2f,.3f,.4f),authored.DiffuseAlbedo);
        Assert.Equal(0,authored.Emissive);

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

    /// <summary>Missing-material transport retains magenta diagnostics after reserving the default interface reflection budget.</summary>
    [Fact]
    public void MissingSurfaceUsesCompensatedMagentaFallback()
    {
        var block=new Block {BlockId=3,Code=new("game","missing"),Textures=new Dictionary<string,CompositeTexture>
            {["all"]=new CompositeTexture(new AssetLocation("game","block/missing"))}};
        var result=BlockFaceDerivedSurfaceLookupBuilder.Build([block],new Dictionary<AssetLocation,PbrMaterialSurface>(),out var stats);
        Vector3 expected=MicrofacetEnergyTable.Shared.DiffuseAlbedo(new(PbrMaterialSurface.Default.Roughness,
            0,0,new(1,0,1),new(.04f)));
        Assert.True(stats.DefaultsUsed>0);
        for(int face=0;face<6;face++)
        {
            Assert.Equal(DerivedSurface.Default,result[18+face]);
            Assert.Equal(expected,result[18+face].DiffuseAlbedo);
            Assert.Equal(new Vector3(.04f),result[18+face].SpecularF0);
        }
        Assert.InRange(expected.X,0.0001f,.9999f);Assert.Equal(0,expected.Y);Assert.Equal(expected.X,expected.Z);
    }
    #endregion
}
