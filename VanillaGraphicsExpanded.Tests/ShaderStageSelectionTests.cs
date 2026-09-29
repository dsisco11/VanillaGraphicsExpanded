using VanillaGraphicsExpanded.HarmonyPatches;

namespace VanillaGraphicsExpanded.Tests;

/// <summary>Checks engine shader stages are parsed only when VGE processing is required.</summary>
public sealed class ShaderStageSelectionTests
{
    [Fact]
    public void RequiresProcessing_UnpatchedImportFreeStage_ReturnsFalse()
    {
        const string source = "#version 330 core\nvoid main() { }\n";

        bool requiresProcessing = EngineShaderProcessingHook.RequiresProcessing("unaffected.vsh", source);

        Assert.False(requiresProcessing);
    }

    [Theory]
    [InlineData("chunkopaque.vsh")]
    [InlineData("chunkopaque.fsh")]
    [InlineData("chunktopsoil.vsh")]
    [InlineData("chunktopsoil.fsh")]
    [InlineData("chunkshadowmap.vsh")]
    [InlineData("final.fsh")]
    [InlineData("sky.fsh")]
    [InlineData("standard.vsh")]
    [InlineData("standard.fsh")]
    [InlineData("entityanimated.vsh")]
    [InlineData("entityanimated.fsh")]
    [InlineData("instanced.vsh")]
    [InlineData("instanced.fsh")]
    [InlineData("chunktransparent.vsh")]
    [InlineData("chunktransparent.fsh")]
    public void RequiresProcessing_PatchedStage_ReturnsTrue(string shaderName)
    {
        bool requiresProcessing = EngineShaderProcessingHook.RequiresProcessing(shaderName, "#version 330 core\n");

        Assert.True(requiresProcessing);
    }

    [Fact]
    public void RequiresProcessing_UnpatchedStageWithImport_ReturnsTrue()
    {
        const string source = "#version 330 core\n@import \"./includes/shared.glsl\"\n";

        bool requiresProcessing = EngineShaderProcessingHook.RequiresProcessing("importonly.gsh", source);

        Assert.True(requiresProcessing);
    }
}