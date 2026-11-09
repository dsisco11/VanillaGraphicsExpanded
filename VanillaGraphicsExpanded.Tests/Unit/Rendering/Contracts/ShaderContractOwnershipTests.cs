using VanillaGraphicsExpanded.LumOn;
using VanillaGraphicsExpanded.LumOn.Scene.Shaders;
using VanillaGraphicsExpanded.PBR;
using VanillaGraphicsExpanded.PBR.Materials;
using VanillaGraphicsExpanded.Rendering.Contracts;
using VanillaGraphicsExpanded.Rendering.Shaders.Fixtures;

namespace VanillaGraphicsExpanded.Tests.Unit.Rendering.Contracts;

/// <summary>Checks that enumeration and runtime owners reuse shader-owned immutable declarations.</summary>
public sealed class ShaderContractOwnershipTests
{
    #region Public API
    #region Ownership
    /// <summary>Runtime owners select their static contracts before API initialization or GPU allocation.</summary>
    [Fact]
    public void RuntimeOwnersSelectTheirOwnDeclarations()
    {
        // Arrange: Construct managed shader owners without initializing graphics resources.
        // Act: Read each owner's selected contract.
        var trace = new LumOnScreenProbeAtlasTraceShaderProgram();
        // Assert: Each owner retains its own declaration instance.
        Assert.Same(LumOnScreenProbeAtlasTraceShaderProgram.Contract, trace.ProgramContract);
        var debug = new LumOnDebugShaderProgram { PassName = LumOnDebugShaderProgram.WorldProbeIrradianceCombinedContract.Identity };
        Assert.Same(LumOnDebugShaderProgram.WorldProbeIrradianceCombinedContract, debug.ProgramContract);
        var bake = new PbrHeightBakeShaderProgram(PbrHeightBakeShaderProgram.CopyContract.Identity, "vanillagraphicsexpanded");
        Assert.Same(PbrHeightBakeShaderProgram.CopyContract, bake.ProgramContract);
    }

    /// <summary>Static shader contracts are the same instances exposed by the shared enumeration.</summary>
    [Fact]
    public void CatalogReferencesShaderOwnedContracts()
    {
        // Arrange: Gather representative production owners from separate shader domains.
        GpuShaderContract[] owned =
        [
            LumOnScreenProbeAtlasTraceShaderProgram.Contract,
            PBRCompositeShaderProgram.Contract,
            LumonSceneCaptureVoxelComputeShader.Contract,
            LumonSceneResetIrradianceComputeShader.Contract,
            RenderInfrastructureShaderProgram.Contract
        ];
        // Act: Resolve each declaration through the generated registry.
        // Assert: The registry returns the exact statically owned contracts.
        foreach (var contract in owned)
            Assert.Same(contract, GpuShaderContracts.Registry.FindProgram(contract.Identity));
        Assert.All(LumOnDebugShaderProgram.Contracts, contract =>
            Assert.Same(contract, GpuShaderContracts.Registry.FindProgram(contract.Identity)));
        Assert.All(PbrHeightBakeShaderProgram.Contracts, contract =>
            Assert.Same(contract, GpuShaderContracts.Registry.FindProgram(contract.Identity)));
    }

    /// <summary>Alternate fixture owners share the production fragment instance and retain their own vertex.</summary>
    [Fact]
    public void FixtureContractsReferenceProductionOwners()
    {
        // Arrange: Use fixture-specific owners that intentionally share production stages.
        // Act: Inspect their stage declarations.
        // Assert: Shared fragments are the same instances while alternate vertices remain explicit.
        Assert.Same(LumOnScreenProbeAtlasTraceShaderProgram.Contract.Stages[1], TraceProbeAnchorShaderProgram.Contract.Stages[1]);
        Assert.Same(PBRDirectLightingShaderProgram.Contract.Stages[1], PbrDirectFullscreenShaderProgram.Contract.Stages[1]);
        Assert.Same(LumOnDebugShaderProgram.WorldProbeIrradianceCombinedContract.Stages[1], WorldProbeDebugShaderProgram.Contract.Stages[1]);
        Assert.Equal("lumon_probe_anchor.vsh", TraceProbeAnchorShaderProgram.Contract.Stages[0].Source);
    }

    /// <summary>Isolated build scopes reuse their shader owners without registering them for packaging.</summary>
    [Fact]
    public void IsolatedScopesReferenceTheirOwnShaders()
    {
        // Arrange: Build the validation-only catalog scope.
        var scope = BuildValidationShaderPrograms.Create();
        // Act: Resolve its programs and query a production-only identity in both scopes.
        // Assert: Validation declarations stay local to their owning scope.
        Assert.Same(BuildValidationGraphicsShader.Contract, scope.FindProgram("fixture"));
        Assert.Same(BuildValidationComputeShader.Contract, scope.FindProgram("fixture_compute"));
        Assert.Throws<ArgumentException>(() => GpuShaderContracts.Registry.FindProgram("fixture"));
    }
    #endregion
    #endregion
}
