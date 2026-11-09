using VanillaGraphicsExpanded.PBR;
using VanillaGraphicsExpanded.PBR.Tessellation;
using Vintagestory.API.Client;

namespace VanillaGraphicsExpanded.Tests.Unit.Terrain;

/// <summary>Checks pass-category rules used by terrain rendering.</summary>
[Collection("CPU state")]
public sealed class TerrainPassDrawTests
{
    #region Public API
    #region Category policy
    /// <summary>Each terrain pass independently determines two-sided and displacement eligibility.</summary>
    [Theory]
    [InlineData(EnumChunkRenderPass.Opaque, false, true)]
    [InlineData(EnumChunkRenderPass.TopSoil, false, true)]
    [InlineData(EnumChunkRenderPass.OpaqueNoCull, true, false)]
    [InlineData(EnumChunkRenderPass.BlendNoCull, true, false)]
    [InlineData(EnumChunkRenderPass.Liquid, false, false)]
    [InlineData(EnumChunkRenderPass.Transparent, false, false)]
    [InlineData(EnumChunkRenderPass.Meta, false, false)]
    [InlineData(EnumChunkRenderPass.OpaqueWaterPlant, false, false)]
    [InlineData(EnumChunkRenderPass.Decor, false, false)]
    public void CategoryPolicies(EnumChunkRenderPass pass, bool twoSided, bool displacement)
    {
        // Arrange the expected policy values in the theory row; these rules have no ambient renderer state.

        // Act and assert the two independent pass policies.
        Assert.Equal(twoSided, TerrainSurfaceNormals.IsTwoSided(pass));
        Assert.Equal(displacement, TerrainDisplacementRuntime.IsEligiblePass(pass));
    }
    #endregion
    #endregion
}
