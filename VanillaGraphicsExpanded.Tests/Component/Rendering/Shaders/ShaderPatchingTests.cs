using System.Collections.Generic;
using System.Linq;
using TinyPreprocessor.Core;
using TinyTokenizer.Ast;
using VanillaGraphicsExpanded;
using VanillaGraphicsExpanded.PBR;
using VanillaGraphicsExpanded.Tests.Support.Rendering.Shaders;
using Xunit;

namespace VanillaGraphicsExpanded.Tests.Component.Rendering.Shaders;

/// <summary>Verifies import preprocessing and coordinated shader syntax edits.</summary>
public sealed class ShaderPatchingTests
{
    #region Public API
    /// <summary>Replaces a valid import with the included GLSL content.</summary>
    [Fact]
    public async Task Preprocessor_InlinesImportContent()
    {
        // Arrange
        var tree = SyntaxTree.Parse(ShaderPatchingTestSources.ShaderWithImport, GlslSchema.Instance);
        var sources = new Dictionary<string, string>
        {
            ["shaders/includes/shared.glsl"] = ShaderPatchingTestSources.SharedInclude
        };

        var resolver = new DictionarySyntaxTreeResourceResolver(sources, ShaderImportsSystem.DefaultDomain);
        var preprocessor = new ShaderSyntaxTreePreprocessor(resolver);
        // Act
        var result = await preprocessor.ProcessAsync(
            new ResourceId($"{ShaderImportsSystem.DefaultDomain}:shaders/test.fsh"),
            tree,
            ct: TestContext.Current.CancellationToken);

        // Assert
        Assert.True(result.Success);

        var output = ShaderPatchingTestSources.NormalizeLineEndings(result.Content.ToText());

        Assert.Contains("float PI = 3.14159", output);
        Assert.DoesNotContain("@import", output);
    }

    /// <summary>Preserves an import-free shader during preprocessing.</summary>
    [Fact]
    public async Task Preprocessor_Succeeds_WhenNoImports()
    {
        // Arrange
        var tree = SyntaxTree.Parse(ShaderPatchingTestSources.SampleShader, GlslSchema.Instance);
        var sources = new Dictionary<string, string>();

        var resolver = new DictionarySyntaxTreeResourceResolver(sources, ShaderImportsSystem.DefaultDomain);
        var preprocessor = new ShaderSyntaxTreePreprocessor(resolver);
        // Act
        var result = await preprocessor.ProcessAsync(
            new ResourceId($"{ShaderImportsSystem.DefaultDomain}:shaders/test.fsh"),
            tree,
            ct: TestContext.Current.CancellationToken);

        // Assert
        Assert.True(result.Success);
        Assert.Equal(ShaderPatchingTestSources.NormalizeLineEndings(tree.ToText().Trim()), ShaderPatchingTestSources.NormalizeLineEndings(result.Content.ToText().Trim()));
    }

    /// <summary>Reports diagnostics when an imported resource is unavailable.</summary>
    [Fact]
    public async Task Preprocessor_Fails_WhenImportNotFound()
    {
        // Arrange
        var tree = SyntaxTree.Parse(ShaderPatchingTestSources.ShaderWithImport, GlslSchema.Instance);
        var sources = new Dictionary<string, string>(); // Empty store

        var resolver = new DictionarySyntaxTreeResourceResolver(sources, ShaderImportsSystem.DefaultDomain);
        var preprocessor = new ShaderSyntaxTreePreprocessor(resolver);
        // Act
        var result = await preprocessor.ProcessAsync(
            new ResourceId($"{ShaderImportsSystem.DefaultDomain}:shaders/test.fsh"),
            tree,
            ct: TestContext.Current.CancellationToken);

        // Assert
        Assert.False(result.Success);
        Assert.NotEmpty(result.Diagnostics);

        var output = string.Join("\n", result.Diagnostics);
        Assert.Contains("shared.glsl", output);
    }

    /// <summary>Processes each referenced resource and removes import directives.</summary>
    [Fact]
    public async Task ProcessImports_HandlesMultipleImports()
    {
        const string multiImportShader = """
            #version 330 core

            @import "./includes/utils.glsl"
            @import "./includes/lighting.glsl"

            void main() {
                fragColor = vec4(1.0);
            }
            """;

        // Arrange
        var tree = SyntaxTree.Parse(multiImportShader, GlslSchema.Instance);
        var sources = new Dictionary<string, string>
        {
            ["shaders/includes/utils.glsl"] = "// Utils\n",
            ["shaders/includes/lighting.glsl"] = "// Lighting\n"
        };

        var resolver = new DictionarySyntaxTreeResourceResolver(sources, ShaderImportsSystem.DefaultDomain);
        var preprocessor = new ShaderSyntaxTreePreprocessor(resolver);
        // Act
        var result = await preprocessor.ProcessAsync(
            new ResourceId($"{ShaderImportsSystem.DefaultDomain}:shaders/test.fsh"),
            tree,
            ct: TestContext.Current.CancellationToken);

        // Assert
        Assert.True(result.Success);

        var output = ShaderPatchingTestSources.NormalizeLineEndings(result.Content.ToText());
        Assert.Contains("// Utils", output);
        Assert.Contains("// Lighting", output);
        Assert.DoesNotContain("@import", output);
    }

    /// <summary>Composes declaration and body edits while preserving original shader code.</summary>
    [Fact]
    public void FullPatchingWorkflow_SimulatesRealUsage()
    {
        // Arrange
        // Simulate what VanillaShaderPatches does
        const string inputShader = """
            #version 330 core

            uniform sampler2D tex;
            out vec4 outColor;

            void main() {
                outColor = texture(tex, vec2(0.5));
            }
            """;

        var tree = SyntaxTree.Parse(inputShader, GlslSchema.Instance);

        // Act
        // Step 1: Insert G-buffer declarations after #version
        var versionQuery = Query.Syntax<GlDirectiveNode>().Named("version");
        tree.CreateEditor()
            .InsertAfter(versionQuery, "\nlayout(location = 4) out vec4 gNormal;")
            .Commit();

        // Step 2: Insert G-buffer writes at function body start
        var mainQuery = Query.Syntax<GlFunctionNode>().Named("main");
        tree.CreateEditor()
            .InsertAfter(mainQuery.InnerStart("body"), "\n    gNormal = vec4(0.0, 1.0, 0.0, 1.0);")
            .Commit();

        var output = ShaderPatchingTestSources.NormalizeLineEndings(tree.ToText());

        // Verify declarations appear (allowing for whitespace variations from tokenizer)
        // Assert
        Assert.Contains("layout", output);
        Assert.Contains("location = 4", output);
        Assert.Contains("gNormal", output);

        // Verify writes appear in main body
        Assert.Contains("gNormal = vec4(0.0, 1.0, 0.0, 1.0);", output);

        // Verify original code is preserved
        Assert.Contains("outColor = texture(tex, vec2(0.5));", output);
    }

    /// <summary>Keeps the version directive at the output start after import processing.</summary>
    [Fact]
    public async Task ProcessImports_PreservesVersionDirective_WithRootToString()
    {
        const string shader = """
            #version 330 core

            uniform sampler2D tex;

            @import "./includes/shared.glsl"

            void main() {
                fragColor = vec4(1.0);
            }
            """;

        // Arrange
        var tree = SyntaxTree.Parse(shader, GlslSchema.Instance);
        var sources = new Dictionary<string, string>
        {
            ["shaders/includes/shared.glsl"] = "// Shared code\nfloat PI = 3.14159;"
        };

        var resolver = new DictionarySyntaxTreeResourceResolver(sources, ShaderImportsSystem.DefaultDomain);
        var preprocessor = new ShaderSyntaxTreePreprocessor(resolver);
        // Act
        var result = await preprocessor.ProcessAsync(
            new ResourceId($"{ShaderImportsSystem.DefaultDomain}:shaders/test.fsh"),
            tree,
            ct: TestContext.Current.CancellationToken);

        // Assert
        Assert.True(result.Success);

        var output = ShaderPatchingTestSources.NormalizeLineEndings(result.Content.ToText());

        // #version directive should be at the start of the output
        Assert.StartsWith("#version 330 core", output);
        Assert.Contains("#version 330 core", output);
    }

    /// <summary>Preserves version and inlines multiple imports in a file-shader-shaped input.</summary>
    [Fact]
    public async Task ProcessImports_FileShaderLikeSource_PreservesVersionDirective()
    {
        const string shader = """
            #version 330 core

            in vec2 uv;
            out vec4 outColor;

            uniform sampler2D primaryScene;
            uniform float zNear;
            uniform float zFar;

            @import "./includes/squirrel3.fsh"
            @import "./includes/pbrFunctions.fsh"

            void main() {
                outColor = texture(primaryScene, uv);
            }
            """;

        // Arrange
        var tree = SyntaxTree.Parse(shader, GlslSchema.Instance);
        var sources = new Dictionary<string, string>
        {
            ["shaders/includes/squirrel3.fsh"] = "// Squirrel3 hash\nfloat hash(vec3 p) { return 0.5; }",
            ["shaders/includes/pbrFunctions.fsh"] = "// PBR functions\nvec3 fresnel(float c, vec3 f) { return f; }"
        };

        var resolver = new DictionarySyntaxTreeResourceResolver(sources, ShaderImportsSystem.DefaultDomain);
        var preprocessor = new ShaderSyntaxTreePreprocessor(resolver);
        // Act
        var result = await preprocessor.ProcessAsync(
            new ResourceId($"{ShaderImportsSystem.DefaultDomain}:shaders/test.fsh"),
            tree,
            ct: TestContext.Current.CancellationToken);

        // Assert
        Assert.True(result.Success);

        var output = ShaderPatchingTestSources.NormalizeLineEndings(result.Content.ToText());

        // Verify #version is preserved
        Assert.StartsWith("#version 330 core", output);

        // Verify imports were processed
        Assert.Contains("// Squirrel3 hash", output);
        Assert.Contains("// PBR functions", output);
        Assert.DoesNotContain("@import", output);
    }
    #endregion
}
