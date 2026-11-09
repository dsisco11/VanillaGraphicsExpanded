using VanillaGraphicsExpanded.HarmonyPatches;

namespace VanillaGraphicsExpanded.Tests.Unit.Rendering.Shaders;

/// <summary>Checks engine shader stages are parsed only when VGE processing is required.</summary>
public sealed class ShaderStageSelectionTests
{
    #region Public API
    /// <summary>Leaves import-free shader stages outside the VGE patch families untouched.</summary>
    [Fact]
    public void RequiresProcessing_UnpatchedImportFreeStage_ReturnsFalse()
    {
        // Arrange
        const string source = "#version 330 core\nvoid main() { }\n";

        // Act
        bool requiresProcessing = EngineShaderProcessingHook.RequiresProcessing("unaffected.vsh", source);

        // Assert
        Assert.False(requiresProcessing);
    }

    /// <summary>Processes every engine stage whose source still receives an owned VGE patch.</summary>
    [Theory]
    [InlineData("chunkopaque.vsh")]
    [InlineData("chunkopaque.fsh")]
    [InlineData("chunktopsoil.vsh")]
    [InlineData("chunktopsoil.fsh")]
    [InlineData("chunkshadowmap.vsh")]
    [InlineData("standard.vsh")]
    [InlineData("standard.fsh")]
    [InlineData("entityanimated.vsh")]
    [InlineData("entityanimated.fsh")]
    [InlineData("instanced.vsh")]
    [InlineData("instanced.fsh")]
    [InlineData("chunktransparent.vsh")]
    [InlineData("chunktransparent.fsh")]
    [InlineData("clouds.fsh")]
    [InlineData("cloudvolumetric.fsh")]
    public void RequiresProcessing_PatchedStage_ReturnsTrue(string shaderName)
    {
        // Arrange
        const string source = "#version 330 core\n";

        // Act
        bool requiresProcessing = EngineShaderProcessingHook.RequiresProcessing(shaderName, source);

        // Assert
        Assert.True(requiresProcessing);
    }

    /// <summary>Resolves VGE imports even when a stage has no source patch.</summary>
    [Fact]
    public void RequiresProcessing_UnpatchedStageWithImport_ReturnsTrue()
    {
        // Arrange
        const string source = "#version 330 core\n@import \"./includes/shared.glsl\"\n";

        // Act
        bool requiresProcessing = EngineShaderProcessingHook.RequiresProcessing("importonly.gsh", source);

        // Assert
        Assert.True(requiresProcessing);
    }

    /// <summary>Owned sky and display replacements leave their import-free engine stages untouched.</summary>
    [Theory]
    [InlineData("sky.vsh")]
    [InlineData("sky.fsh")]
    [InlineData("nightsky.vsh")]
    [InlineData("nightsky.fsh")]
    [InlineData("celestialobject.vsh")]
    [InlineData("celestialobject.fsh")]
    [InlineData("final.fsh")]
    public void RequiresProcessing_ReplacedEngineStage_ReturnsFalse(string shaderName)
    {
        // Arrange each owned replacement stage name from the theory rows.
        // Act
        bool requiresProcessing = EngineShaderProcessingHook.RequiresProcessing(shaderName, "#version 330 core\n");

        // Assert
        Assert.False(requiresProcessing);
    }

    /// <summary>Native geometry and the retained pre-lit map producer keep their own code; restored visible fragments retain scene conversion.</summary>
    [Theory]
    [InlineData("clouds.vsh")]
    [InlineData("cloudmap.vsh")]
    [InlineData("cloudmap.fsh")]
    [InlineData("cloudvolumetric.vsh")]
    public void RequiresProcessing_NativeCloudProducerAndGeometryRemainUntouched(string shaderName)
    {
        // Arrange each native cloud producer or geometry stage name from the theory rows.
        // Act
        bool requiresProcessing = EngineShaderProcessingHook.RequiresProcessing(shaderName, "#version 330 core\n");

        // Assert
        Assert.False(requiresProcessing);
    }
    #endregion
}
