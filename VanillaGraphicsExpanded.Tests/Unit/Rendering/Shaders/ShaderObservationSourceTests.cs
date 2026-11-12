using TinyTokenizer.Ast;
using VgeShaderTestPreparation;
using Xunit;

namespace VanillaGraphicsExpanded.Tests.Unit.Rendering.Shaders;

/// <summary>Protects typed AST capture of offline shadow transform-feedback observations.</summary>
public sealed class ShaderObservationSourceTests
{
    #region Public API
    /// <summary>Decorates the exact typed output and replaces only the named main body.</summary>
    [Fact]
    public void Capture_UsesTypedOutputAndPreservesUnrelatedSyntaxAndMainTrivia()
    {
        // Arrange: comments, member names, helper functions and an existing layout resemble target identifiers.
        const string source = """
            #version 450 core
            // out vec4 vge_shadowPosition; vgeShadowMaterialMain();
            struct Material { vec4 vge_shadowPosition; };
            layout(location=3) out vec4 vge_shadowPosition;
            void vgeShadowMaterialMain() { Material m; m.vge_shadowPosition = vec4(1.0); }
            // before main
            void main() { int vge_shadowPositionLocal = 1; }
            // after main
            """;

        // Act
        string captured = ShaderObservationSource.Capture(source);

        // Assert: only the output declaration receives feedback layout and the wrapper owns main's executable body.
        var tree = SyntaxTree.Parse(captured, GlslSchema.Instance);
        var output = Assert.Single(tree.Root.Children.OfType<GlStageIoNode>());
        Assert.Equal("vge_shadowPosition", output.Name);
        Assert.Contains("layout(xfb_buffer=0, xfb_offset=0, xfb_stride=16)", captured);
        Assert.Contains("layout(location=3)", captured);
        Assert.Contains("vec4 vge_shadowPosition;", captured); // Struct member is untouched.
        Assert.Contains("m.vge_shadowPosition = vec4(1.0);", captured);
        Assert.Contains("// out vec4 vge_shadowPosition; vgeShadowMaterialMain();", captured);
        Assert.Contains("// before main", captured);
        Assert.Contains("// after main", captured);
        Assert.DoesNotContain("vge_shadowPositionLocal", captured);
        Assert.Contains("vgeShadowMaterialMain();", captured);
        Assert.Contains("gl_Position = vge_shadowPosition;", captured);
    }

    /// <summary>Rejects declarations that do not define one scalar vec4 output with the exact owned name.</summary>
    [Theory]
    [InlineData("in vec4 vge_shadowPosition;")]
    [InlineData("out vec3 vge_shadowPosition;")]
    [InlineData("out vec4 vge_shadowPosition[2];")]
    [InlineData("out vec4 vge_shadowPosition; out vec4 vge_shadowPosition;")]
    public void Capture_RejectsWrongOrAmbiguousOutputDeclaration(string declarations)
    {
        // Arrange
        string source = Source(declarations);

        // Act and assert: the typed declaration contract rejects these before emitting a feedback stage.
        Assert.Throws<InvalidDataException>(() => ShaderObservationSource.Capture(source));
    }

    /// <summary>Does not treat declaration-shaped comments or unrelated identifiers as a valid output declaration.</summary>
    [Fact]
    public void Capture_RejectsCommentOnlyOutputAndMaterialFunctionNames()
    {
        // Arrange
        const string source = """
            #version 450 core
            // out vec4 vge_shadowPosition;
            void vgeShadowMaterialMain() { }
            void main() { }
            """;

        // Act and assert
        Assert.Throws<InvalidDataException>(() => ShaderObservationSource.Capture(source));
    }
    #endregion

    #region Private
    /// <summary>Builds the smallest producer-like source accepted by the capture utility.</summary>
    private static string Source(string declarations) => $$"""
        #version 450 core
        {{declarations}}
        void vgeShadowMaterialMain() { }
        void main() { }
        """;

    #endregion
}
