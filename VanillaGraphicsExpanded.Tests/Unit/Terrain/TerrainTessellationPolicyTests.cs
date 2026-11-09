using HarmonyLib;
using VanillaGraphicsExpanded.HarmonyPatches;
using VanillaGraphicsExpanded.PBR.Tessellation;
using VanillaGraphicsExpanded.Rendering;
using Vintagestory.Client.NoObf;

namespace VanillaGraphicsExpanded.Tests.Unit.Terrain;

/// <summary>Checks terrain displacement and tessellation policy rules with synthetic inputs.</summary>
[Collection("CPU state")]
public sealed class TerrainTessellationPolicyTests
{
    #region Public API
    #region Displacement policy
    /// <summary>Admission expands a copied engine volume by the maximum displacement on every axis.</summary>
    [Fact]
    public void AdmissionBoundsReserveMaximumDisplacement()
    {
        // Arrange one engine volume and retain its original dimensions for the copy-preservation check.
        var original = new Vintagestory.API.MathTools.Sphere { radius = 1, radiusY = 2, radiusZ = 3 };
        var expanded = original;

        // Act on the copied admission volume through the production hook.
        TerrainDisplacementBoundsHook.Prefix(ref expanded);

        // Assert the source remains unchanged and each expanded axis includes the displacement margin.
        Assert.Equal(1, original.radius);
        Assert.Equal(2, original.radiusY);
        Assert.Equal(3, original.radiusZ);
        Assert.InRange(expanded.radius, 1.04999, 1.05001);
        Assert.InRange(expanded.radiusY, 2.04999, 2.05001);
        Assert.InRange(expanded.radiusZ, 3.04999, 3.05001);
    }

    /// <summary>Shadow displacement is eligible only for opaque and topsoil pool identities.</summary>
    [Fact]
    public void ShadowPoolProvenanceExcludesOtherTerrainPasses()
    {
        // Arrange the three representative installed pass identities.

        // Act and assert that only the intended shadow pool families carry displacement eligibility.
        Assert.True(TerrainDisplacementRuntime.IsEligiblePass(Vintagestory.API.Client.EnumChunkRenderPass.Opaque));
        Assert.True(TerrainDisplacementRuntime.IsEligiblePass(Vintagestory.API.Client.EnumChunkRenderPass.TopSoil));
        Assert.False(TerrainDisplacementRuntime.IsEligiblePass(Vintagestory.API.Client.EnumChunkRenderPass.Liquid));
    }
    #endregion

    #region Selection and draw layout
    /// <summary>Only opaque and topsoil terrain families are eligible for tessellation.</summary>
    [Theory]
    [InlineData("chunkopaque", true)]
    [InlineData("chunktopsoil", true)]
    [InlineData("chunktransparent", false)]
    [InlineData("chunkliquid", false)]
    [InlineData("standard", false)]
    [InlineData(null, false)]
    public void EligibilityMatchesTerrainScope(string? name, bool expected)
    {
        // Arrange each pass name and expected eligibility in the theory row.

        // Act and assert the terrain-specific selection rule.
        Assert.Equal(expected, TerrainTessellationPrograms.Eligible(name));
    }

    /// <summary>Both topology fields receive the selector without replacing original instruction objects.</summary>
    [Fact]
    public void GroupedDrawRewritesBothTopologyLoads()
    {
        // Arrange two synthetic field loads with a non-field instruction between them.
        var field = AccessTools.Field(typeof(VAO), nameof(VAO.drawMode));
        var first = new CodeInstruction(System.Reflection.Emit.OpCodes.Ldfld, field);
        var second = new CodeInstruction(System.Reflection.Emit.OpCodes.Ldfld, field);
        var input = new[] { first, new CodeInstruction(System.Reflection.Emit.OpCodes.Nop), second };

        bool previousAvailability = TerrainTessellationPrograms.DrawHookAvailable;
        try
        {
            // Act by applying the production draw selector rewrite.
            var actual = TerrainTessellationDrawHook.Transpiler(input).ToArray();

            // Assert both original loads remain and each receives the same topology selector call.
            Assert.Equal(5, actual.Length);
            Assert.Same(first, actual[0]);
            Assert.Same(second, actual[3]);
            Assert.Equal(System.Reflection.Emit.OpCodes.Call, actual[1].opcode);
            Assert.Equal(System.Reflection.Emit.OpCodes.Call, actual[4].opcode);
            Assert.Equal(AccessTools.Method(typeof(TerrainTessellationPrograms), nameof(TerrainTessellationPrograms.Topology)), actual[1].operand);
            Assert.Equal(actual[1].operand, actual[4].operand);
        }
        finally
        {
            TerrainTessellationPrograms.DrawHookAvailable = previousAvailability;
        }
    }

    /// <summary>Unexpected draw layout keeps ordinary drawing and disables the unsupported rewrite.</summary>
    [Fact]
    public void ChangedGroupedDrawLayoutIsRejected()
    {
        // Arrange a body without either expected topology field load.
        var original = new[] { new CodeInstruction(System.Reflection.Emit.OpCodes.Nop) };

        bool previousAvailability = TerrainTessellationPrograms.DrawHookAvailable;
        try
        {
            // Act by applying the production transpiler to the unsupported layout.
            var actual = TerrainTessellationDrawHook.Transpiler(original).ToArray();

            // Assert the original instruction remains and the hook reports the unsupported layout.
            Assert.Equal(original, actual);
            Assert.False(TerrainTessellationPrograms.DrawHookAvailable);
        }
        finally
        {
            TerrainTessellationPrograms.DrawHookAvailable = previousAvailability;
        }
    }
    #endregion
    #endregion
}
