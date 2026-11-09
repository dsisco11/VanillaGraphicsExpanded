using System.Text.RegularExpressions;
using TinyTokenizer.Ast;
using VanillaGraphicsExpanded.Tests.Support.Rendering.Shaders;
using Xunit;

namespace VanillaGraphicsExpanded.Tests.Unit.Rendering.Shaders;

/// <summary>Verifies syntax-tree editor insertion boundaries and commit behavior.</summary>
public sealed class GLSLASTEditRulesTests
{
    #region Public API
    /// <summary>Preserves inserted text and the main function after editing its syntax tree.</summary>
    [Fact]
    public void Insert_BeforeFunction_PreservesInsertedTextAndMain()
    {
        // Arrange
        var tree = SyntaxTree.Parse(ShaderPatchingTestSources.SampleShader, GlslSchema.Instance);

        // Act
        tree.CreateEditor()
            .InsertBefore(Query.Syntax<GlFunctionNode>().Named("main"), "// Before main\n")
            .Commit();

        var output = ShaderPatchingTestSources.NormalizeLineEndings(tree.ToText());
        // Roslyn-style: insertion goes before target's leading trivia
        // The comment should appear somewhere before the main function
        // Assert
        Assert.Contains("// Before main", output);
        // main should still exist
        Assert.Contains("void main()", output);
    }

    /// <summary>Inserts text after the version directive.</summary>
    [Fact]
    public void Insert_AfterVersionDirective_InsertsAtCorrectPosition()
    {
        // Arrange
        var tree = SyntaxTree.Parse(ShaderPatchingTestSources.SampleShader, GlslSchema.Instance);

        // Act
        tree.CreateEditor()
            .InsertAfter(Query.Syntax<GlDirectiveNode>().Named("version"), "\n// After version")
            .Commit();

        var output = ShaderPatchingTestSources.NormalizeLineEndings(tree.ToText());
        // Assert
        Assert.Matches(@"#version 330 core\s*// After version", output);
    }

    /// <summary>Inserts text at the beginning of a function body.</summary>
    [Fact(Skip = "Bug in TinyTokenizer - InsertAfter makes inserted content consume the leading trivia of the next node")]
    public void Insert_InnerStartBody_InsertsAfterOpeningBrace()
    {
        // Arrange
        var tree = SyntaxTree.Parse(ShaderPatchingTestSources.SampleShader, GlslSchema.Instance);

        // Act
        tree.CreateEditor()
            .InsertAfter(Query.Syntax<GlFunctionNode>().Named("main").InnerStart("body"), "    // Body start\n")
            .Commit();

        var output = ShaderPatchingTestSources.NormalizeLineEndings(tree.ToText());
        // Assert
        Assert.Contains("void main() {\n    // Body start", output);
    }

    /// <summary>Inserts text at the end of a function body.</summary>
    [Fact]
    public void Insert_InnerEndBody_InsertsBeforeClosingBrace()
    {
        // Arrange
        var tree = SyntaxTree.Parse(ShaderPatchingTestSources.SampleShader, GlslSchema.Instance);

        // Act
        tree.CreateEditor()
            .InsertBefore(Query.Syntax<GlFunctionNode>().Named("main").InnerEnd("body"), "\n    // Body end")
            .Commit();

        var output = ShaderPatchingTestSources.NormalizeLineEndings(tree.ToText());
        // Assert
        Assert.Matches(@"// Body end\s*}", output);
    }

    /// <summary>Applies several syntax edits through one editor commit.</summary>
    [Fact]
    public void MultipleEdits_ApplyInSingleCommit()
    {
        // Arrange
        var tree = SyntaxTree.Parse(ShaderPatchingTestSources.SampleShader, GlslSchema.Instance);

        var mainQuery = Query.Syntax<GlFunctionNode>().Named("main");
        var versionQuery = Query.Syntax<GlDirectiveNode>().Named("version");

        // Act
        tree.CreateEditor()
            .InsertAfter(versionQuery, "\n// Layout declarations")
            .InsertAfter(mainQuery.InnerStart("body"), "\n    // Init code")
            .InsertBefore(mainQuery.InnerEnd("body"), "\n    // Cleanup code")
            .Commit();

        var output = ShaderPatchingTestSources.NormalizeLineEndings(tree.ToText());

        // Assert
        Assert.Contains("// Layout declarations", output);
        Assert.Contains("// Init code", output);
        Assert.Contains("// Cleanup code", output);
    }
    #endregion
}
