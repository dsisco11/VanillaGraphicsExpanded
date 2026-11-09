using System.Linq;
using TinyTokenizer.Ast;
using VanillaGraphicsExpanded.Tests.Support.Rendering.Shaders;
using Xunit;

namespace VanillaGraphicsExpanded.Tests.Unit.Rendering.Shaders;

/// <summary>Verifies parsed GLSL functions and directives through the tokenizer syntax tree.</summary>
public sealed class GLSLParserRulesTests
{
    #region Public API
    /// <summary>Recognizes the sample shader main and helper function declarations.</summary>
    [Fact]
    public void Parse_RecognizesGlslFunctions()
    {
        // Arrange
        const string shader = ShaderPatchingTestSources.SampleShader;
        // Act
        var tree = SyntaxTree.Parse(shader, GlslSchema.Instance);

        var functions = tree.Select(Query.Syntax<GlFunctionNode>())
            .Cast<GlFunctionNode>()
            .ToList();

        // Assert
        Assert.Equal(2, functions.Count);
        Assert.Contains(functions, f => f.Name == "main" && f.ReturnType == "void");
        Assert.Contains(functions, f => f.Name == "sampleTexture" && f.ReturnType == "vec4");
    }

    /// <summary>Preserves the version directive name and parsed arguments.</summary>
    [Fact]
    public void Parse_RecognizesVersionDirective()
    {
        // Arrange
        const string shader = ShaderPatchingTestSources.SampleShader;
        // Act
        var tree = SyntaxTree.Parse(shader, GlslSchema.Instance);

        var versionDirective = tree.Select(Query.Syntax<GlDirectiveNode>().Named("version"))
            .Cast<GlDirectiveNode>()
            .FirstOrDefault();

        // Assert
        Assert.NotNull(versionDirective);
        Assert.Equal("version", versionDirective.Name);
        Assert.Equal("330 core\n", ShaderPatchingTestSources.NormalizeLineEndings(versionDirective.ArgumentsText));
    }

    /// <summary>Recognizes the import path and preserves its directive text.</summary>
    [Fact]
    public void Parse_RecognizesImportDirective()
    {
        // Arrange
        const string shader = ShaderPatchingTestSources.ShaderWithImport;
        // Act
        var tree = SyntaxTree.Parse(shader, GlslSchema.Instance);

        var importDirective = tree.Select(Query.Syntax<GlImportNode>()).FirstOrDefault() as GlImportNode;

        // Assert
        Assert.NotNull(importDirective);
        Assert.Equal("import", importDirective.Name);
        Assert.Equal("./includes/shared.glsl", ShaderPatchingTestSources.NormalizeLineEndings(importDirective.ImportString));
        Assert.Equal("\n@import \"./includes/shared.glsl\"\n", ShaderPatchingTestSources.NormalizeLineEndings(importDirective.ToText()));
    }

    /// <summary>Exposes named body and parameter blocks for a parsed function.</summary>
    [Fact]
    public void GlslFunctionSyntax_ProvidesBlockAccess()
    {
        // Arrange
        const string shader = ShaderPatchingTestSources.SampleShader;
        // Act
        var tree = SyntaxTree.Parse(shader, GlslSchema.Instance);

        var mainFunc = tree.Select(Query.Syntax<GlFunctionNode>().Named("main"))
            .Cast<GlFunctionNode>()
            .FirstOrDefault();

        // Assert
        Assert.NotNull(mainFunc);
        Assert.NotNull(mainFunc.Body);
        Assert.NotNull(mainFunc.Parameters);
        Assert.Contains("body", mainFunc.BlockNames);
        Assert.Contains("params", mainFunc.BlockNames);
    }

    /// <summary>Retains the version directive when parsing without edits.</summary>
    [Fact]
    public void Parse_PreservesVersionDirective_WithoutModifications()
    {
        // Arrange
        const string shader = """
            #version 330 core

            void main() {
                fragColor = vec4(1.0);
            }
            """;

        // Act
        var tree = SyntaxTree.Parse(shader, GlslSchema.Instance);

        // Read the parsed tree's emitted text to verify it preserves the source directive.
        var rootOutput = ShaderPatchingTestSources.NormalizeLineEndings(tree.ToText());

        // Assert
        Assert.StartsWith("#version 330 core", rootOutput);
    }

    /// <summary>Creates a syntax tree for empty shader source.</summary>
    [Fact]
    public void Parse_EmptyShader_DoesNotThrow()
    {
        // Arrange
        const string shader = "";
        // Act
        var tree = SyntaxTree.Parse(shader, GlslSchema.Instance);
        // Assert
        Assert.NotNull(tree);
    }

    /// <summary>Ignores line and block comments when querying shader functions.</summary>
    [Fact]
    public void Parse_ShaderWithComments_IgnoresComments()
    {
        // Arrange
        const string shaderWithComments = """
            #version 330 core
            // This is a comment
            /* Multi-line
               comment */
            void main() {
                // Inside function
            }
            """;

        // Act
        var tree = SyntaxTree.Parse(shaderWithComments, GlslSchema.Instance);

        var functions = tree.Select(Query.Syntax<GlFunctionNode>())
            .Cast<GlFunctionNode>()
            .ToList();

        // Assert
        Assert.Single(functions);
        Assert.Equal("main", functions[0].Name);
    }

    /// <summary>Returns no function nodes for an unmatched function name.</summary>
    [Fact]
    public void Query_NonExistentFunction_ReturnsEmpty()
    {
        // Arrange
        const string shader = ShaderPatchingTestSources.SampleShader;
        // Act
        var tree = SyntaxTree.Parse(shader, GlslSchema.Instance);

        var result = tree.Select(Query.Syntax<GlFunctionNode>().Named("nonexistent"))
            .Cast<GlFunctionNode>()
            .ToList();

        // Assert
        Assert.Empty(result);
    }
    #endregion
}
