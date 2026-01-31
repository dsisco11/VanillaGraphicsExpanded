using TinyTokenizer.Ast;

using VanillaGraphicsExpanded.PBR;

using Xunit;

namespace VanillaGraphicsExpanded.Tests;

public sealed class VanillaShaderPatchesPreProcessingTests
{
    [Fact]
    public void TryApplyPreProcessing_ChunkShader_InsertsLumonSceneChunkSlotInclude()
    {
        const string shader = """
            #version 330 core

            void main() {
            }
            """;

        var tree = SyntaxTree.Parse(shader, GlslSchema.Instance);
        bool applied = VanillaShaderPatches.TryApplyPreProcessing(log: null, tree, sourceName: "chunkopaque.fsh");

        Assert.True(applied);
        Assert.Contains("@import \"./includes/lumonscene_chunkslot.glsl\"", tree.ToText());
    }
}

