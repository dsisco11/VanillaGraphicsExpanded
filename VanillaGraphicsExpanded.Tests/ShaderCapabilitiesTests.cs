using VanillaGraphicsExpanded.HarmonyPatches;
using VanillaGraphicsExpanded.Rendering.Shaders;
using Vintagestory.Client.NoObf;

namespace VanillaGraphicsExpanded.Tests;

/// <summary>Verifies feature publication uses linked executable identity rather than shader names or uniforms.</summary>
public sealed class ShaderCapabilitiesTests
{
    #region Publication and identity
    /// <summary>Scene color support belongs to one successfully linked executable and is withdrawn on reload or failure.</summary>
    [Fact]
    public void SceneColorConventionRequiresCurrentSuccessfulExecutable()
    {
        var program = new ShaderProgram { ProgramId = 10 };
        ShaderCapabilities.Declare(program, ShaderCapability.SceneColorConvention);
        Assert.False(ShaderCapabilities.Has(program, ShaderCapability.SceneColorConvention));

        bool success = true;
        ShaderPatchCompilationHook.Finalizer(program, ref success, null);
        Assert.True(ShaderCapabilities.Has(program, ShaderCapability.SceneColorConvention));

        program.ProgramId = 11;
        Assert.False(ShaderCapabilities.Has(program, ShaderCapability.SceneColorConvention));
        ShaderCapabilities.PublishDeclared(program);
        Assert.True(ShaderCapabilities.Has(program, ShaderCapability.SceneColorConvention));

        success = false;
        ShaderPatchCompilationHook.Finalizer(program, ref success, null);
        Assert.False(ShaderCapabilities.Has(program, ShaderCapability.SceneColorConvention));
        ShaderCapabilities.Forget(program);
        ShaderCapabilities.PublishDeclared(program);
        Assert.False(ShaderCapabilities.Has(program, ShaderCapability.SceneColorConvention));
    }

    /// <summary>Source declarations become visible only at the successful compile boundary.</summary>
    [Fact]
    public void DeclarationRequiresSuccessfulCompilation()
    {
        var program = new ShaderProgram { ProgramId = 10 };
        ShaderCapabilities.Declare(program, ShaderCapability.TwoSidedSurfaceNormals);
        Assert.False(ShaderCapabilities.Has(program, ShaderCapability.TwoSidedSurfaceNormals));
        bool success = true;
        Assert.Null(ShaderPatchCompilationHook.Finalizer(program, ref success, null));
        Assert.True(ShaderCapabilities.Has(program, ShaderCapability.TwoSidedSurfaceNormals));
        var other = new ShaderProgram { ProgramId = 10 };
        Assert.False(ShaderCapabilities.Has(other, ShaderCapability.TwoSidedSurfaceNormals));
        program.ProgramId = 11;
        Assert.False(ShaderCapabilities.Has(program, ShaderCapability.TwoSidedSurfaceNormals));
        program.ProgramId = 0;
        Assert.False(ShaderCapabilities.Has(program, ShaderCapability.TwoSidedSurfaceNormals));
    }

    /// <summary>Displacement installation and fragment publication share identity without clobbering each other.</summary>
    [Fact]
    public void FeaturesComposeAndReplacementDoesNotInheritOldFeatures()
    {
        var program = new ShaderProgram { ProgramId = 10 };
        ShaderCapabilities.Declare(program, ShaderCapability.TwoSidedSurfaceNormals);
        ShaderCapabilities.Publish(program, ShaderCapability.TerrainDisplacement);
        ShaderCapabilities.PublishDeclared(program);
        Assert.True(ShaderCapabilities.Has(program, ShaderCapability.TerrainDisplacement | ShaderCapability.TwoSidedSurfaceNormals));
        ShaderCapabilities.Remove(program, ShaderCapability.TerrainDisplacement);
        Assert.True(ShaderCapabilities.Has(program, ShaderCapability.TwoSidedSurfaceNormals));
        Assert.False(ShaderCapabilities.Has(program, ShaderCapability.TerrainDisplacement));
        program.ProgramId = 11;
        ShaderCapabilities.Publish(program, ShaderCapability.TerrainDisplacement);
        Assert.False(ShaderCapabilities.Has(program, ShaderCapability.TwoSidedSurfaceNormals));
        ShaderCapabilities.Forget(program);
        ShaderCapabilities.PublishDeclared(program);
        Assert.False(ShaderCapabilities.Has(program, ShaderCapability.TerrainDisplacement));
        Assert.False(ShaderCapabilities.Has(program, ShaderCapability.TwoSidedSurfaceNormals));
    }

    /// <summary>A failed compile cannot expose capabilities from a partially installed candidate.</summary>
    [Fact]
    public void FailedCompileWithdrawsCandidateFeatures()
    {
        var program = new ShaderProgram { ProgramId = 10 };
        ShaderCapabilities.Publish(program, ShaderCapability.TerrainDisplacement);
        bool success = false;
        Assert.Null(ShaderPatchCompilationHook.Finalizer(program, ref success, null));
        Assert.False(ShaderCapabilities.Has(program, ShaderCapability.TerrainDisplacement));
    }
    #endregion
}
