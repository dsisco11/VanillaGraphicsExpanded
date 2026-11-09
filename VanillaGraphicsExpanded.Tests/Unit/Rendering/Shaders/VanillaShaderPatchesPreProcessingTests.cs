using TinyTokenizer.Ast;

using VanillaGraphicsExpanded.PBR;

using Xunit;

namespace VanillaGraphicsExpanded.Tests.Unit.Rendering.Shaders;

/// <summary>Verifies vanilla shader preprocessing inserts the declared LumOn include.</summary>
public sealed class VanillaShaderPatchesPreProcessingTests
{
    #region Public API
    /// <summary>Applies the chunk shader preprocessing rule and checks include order.</summary>
    [Fact]
    public void TryApplyPreProcessing_ChunkShader_InsertsLumonSceneChunkSlotInclude()
    {
        // Arrange
        const string shader = """
            #version 330 core

            void main() {
            }
            """;

        var tree = SyntaxTree.Parse(shader, GlslSchema.Instance);

        // Act
        bool applied = VanillaShaderPatches.TryApplyPreProcessing(log: null, tree, sourceName: "chunkopaque.fsh");

        // Assert
        Assert.True(applied);
        Assert.Contains("@import \"./includes/lumonscene_chunkslot.glsl\"", tree.ToText());
        Assert.True(tree.ToText().IndexOf("VGE_PBR_ENABLE_NORMAL_MAPS", StringComparison.Ordinal)
            < tree.ToText().IndexOf("@import \"./includes/vsfunctions.glsl\"", StringComparison.Ordinal));
    }
    #endregion
}

