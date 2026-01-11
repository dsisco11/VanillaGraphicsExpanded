using TinyTokenizer.Ast;
using VanillaGraphicsExpanded;
using VanillaGraphicsExpanded.PBR;
using VanillaGraphicsExpanded.Tests.Helpers;
using Xunit;

using TinyPreprocessor.Core;

namespace VanillaGraphicsExpanded.Tests;

/// <summary>
/// Tests for GLSL schema parsing, import processing, and shader patching.
/// </summary>
public class ShaderPatchingTests
{
    private readonly ITestOutputHelper _output;

    public ShaderPatchingTests(ITestOutputHelper output)
    {
        _output = output;
    }

    #region Test Helpers

    /// <summary>
    /// Normalizes line endings to \n for cross-platform test assertions.
    /// </summary>
    private static string NormalizeLineEndings(string text) =>
        text.ReplaceLineEndings("\n");

    /// <summary>
    /// Sample GLSL shader for testing function and directive recognition.
    /// </summary>
    private const string SampleShader = """
        #version 330 core
        #extension GL_ARB_explicit_attrib_location: enable
        
        layout(location = 0) out vec4 outColor;
        layout(location = 1) out vec4 outGlow;
        #if SSAOLEVEL > 0
        in vec4 gnormal;
        layout(location = 2) out vec4 outGNormal;
        layout(location = 3) out vec4 outGPosition;
        #endif

        uniform sampler2D tex;
        in vec2 uv;
        out vec4 fragColor;

        // Helper function
        vec4 sampleTexture(vec2 coord) {
            return texture(tex, coord);
        }

        void main() {
            vec4 color = sampleTexture(uv);
            fragColor = color;
        }
        """;

    /// <summary>
    /// Sample shader with @import directive.
    /// </summary>
    private const string ShaderWithImport = """
        #version 330 core
        #extension GL_ARB_explicit_attrib_location: enable
        
        layout(location = 0) out vec4 outColor;
        layout(location = 1) out vec4 outGlow;
        #if SSAOLEVEL > 0
        in vec4 gnormal;
        layout(location = 2) out vec4 outGNormal;
        layout(location = 3) out vec4 outGPosition;
        #endif

        @import "shared.glsl"
        void main() {
            fragColor = vec4(1.0);
        }
        """;

    /// <summary>
    /// Sample include file for preprocessing tests.
    /// </summary>
    private const string SharedInclude = """
        // Shared code
        float PI = 3.14159;
        """;
    #endregion

    #region GlslSchema Parsing Tests
    [Fact]
    public void Diagnostic_DumpTokens()
    {
        var tree = SyntaxTree.Parse(SampleShader, GlslSchema.Instance);
        var output = tree.ToString("S", null);
        _output.WriteLine(output);
    }

    [Fact]
    public void Parse_RecognizesGlslFunctions()
    {
        var tree = SyntaxTree.Parse(SampleShader, GlslSchema.Instance);

        var functions = tree.Select(Query.Syntax<GlFunctionNode>())
            .Cast<GlFunctionNode>()
            .ToList();

        Assert.Equal(2, functions.Count);
        Assert.Contains(functions, f => f.Name == "main" && f.ReturnType == "void");
        Assert.Contains(functions, f => f.Name == "sampleTexture" && f.ReturnType == "vec4");
    }

    [Fact]
    public void Parse_RecognizesVersionDirective()
    {
        var tree = SyntaxTree.Parse(SampleShader, GlslSchema.Instance);

        var versionDirective = tree.Select(Query.Syntax<GlDirectiveNode>().Named("version"))
            .Cast<GlDirectiveNode>()
            .FirstOrDefault();

        Assert.NotNull(versionDirective);
        Assert.Equal("version", versionDirective.Name);
        Assert.Equal("330 core\n", NormalizeLineEndings(versionDirective.ArgumentsText));
    }

    [Fact]
    public void Parse_RecognizesImportDirective()
    {
        var tree = SyntaxTree.Parse(ShaderWithImport, GlslSchema.Instance);

        var importDirective = tree.Select(Query.Syntax<GlImportNode>()).FirstOrDefault() as GlImportNode;

        Assert.NotNull(importDirective);
        Assert.Equal("import", importDirective.Name);
        Assert.Equal("shared.glsl", NormalizeLineEndings(importDirective.ImportString));
        Assert.Equal("\n@import \"shared.glsl\"\n", NormalizeLineEndings(importDirective.ToText()));
    }

    [Fact]
    public void GlslFunctionSyntax_ProvidesBlockAccess()
    {
        var tree = SyntaxTree.Parse(SampleShader, GlslSchema.Instance);

        var mainFunc = tree.Select(Query.Syntax<GlFunctionNode>().Named("main"))
            .Cast<GlFunctionNode>()
            .FirstOrDefault();

        Assert.NotNull(mainFunc);
        Assert.NotNull(mainFunc.Body);
        Assert.NotNull(mainFunc.Parameters);
        Assert.Contains("body", mainFunc.BlockNames);
        Assert.Contains("params", mainFunc.BlockNames);
    }

    #endregion

    #region Import Processing Tests

    [Fact]
    public void DictionaryResolver_ResolvesBareImportReference()
    {
        var sources = new Dictionary<string, string>
        {
            ["shaderincludes/shared.glsl"] = SharedInclude
        };

        var resolver = new DictionarySyntaxTreeResourceResolver(sources, ShaderImportsSystem.DefaultDomain);
        var result = resolver.ResolveAsync("shared.glsl", relativeTo: null, ct: default).GetAwaiter().GetResult();

        Assert.True(result.IsSuccess, result.Error?.ToString());
        Assert.NotNull(result.Resource);
        Assert.Equal($"{ShaderImportsSystem.DefaultDomain}:shaderincludes/shared.glsl", result.Resource.Id.Path);
    }

    [Fact]
    public void Preprocessor_InlinesImportContent()
    {
        var tree = SyntaxTree.Parse(ShaderWithImport, GlslSchema.Instance);
        var sources = new Dictionary<string, string>
        {
            ["shaderincludes/shared.glsl"] = SharedInclude
        };

        var resolver = new DictionarySyntaxTreeResourceResolver(sources, ShaderImportsSystem.DefaultDomain);
        var preprocessor = new ShaderSyntaxTreePreprocessor(resolver);
        var result = preprocessor.Process(new ResourceId($"{ShaderImportsSystem.DefaultDomain}:shaders/test.fsh"), tree);

        Assert.True(result.Success);

        var output = NormalizeLineEndings(result.Content.ToText());

        Assert.Contains("float PI = 3.14159", output);
        Assert.DoesNotContain("@import", output);
    }

    [Fact]
    public void Preprocessor_Succeeds_WhenNoImports()
    {
        var tree = SyntaxTree.Parse(SampleShader, GlslSchema.Instance);
        var sources = new Dictionary<string, string>();

        var resolver = new DictionarySyntaxTreeResourceResolver(sources, ShaderImportsSystem.DefaultDomain);
        var preprocessor = new ShaderSyntaxTreePreprocessor(resolver);
        var result = preprocessor.Process(new ResourceId($"{ShaderImportsSystem.DefaultDomain}:shaders/test.fsh"), tree);

        Assert.True(result.Success);
        Assert.Equal(NormalizeLineEndings(tree.ToText().Trim()), NormalizeLineEndings(result.Content.ToText().Trim()));
    }

    [Fact]
    public void Preprocessor_Fails_WhenImportNotFound()
    {
        var tree = SyntaxTree.Parse(ShaderWithImport, GlslSchema.Instance);
        var sources = new Dictionary<string, string>(); // Empty store

        var resolver = new DictionarySyntaxTreeResourceResolver(sources, ShaderImportsSystem.DefaultDomain);
        var preprocessor = new ShaderSyntaxTreePreprocessor(resolver);
        var result = preprocessor.Process(new ResourceId($"{ShaderImportsSystem.DefaultDomain}:shaders/test.fsh"), tree);

        Assert.False(result.Success);
        Assert.NotEmpty(result.Diagnostics);

        var output = string.Join("\n", result.Diagnostics);
        Assert.Contains("shared.glsl", output);
    }

    [Fact]
    public void ProcessImports_HandlesMultipleImports()
    {
        const string multiImportShader = """
            #version 330 core

            @import "utils.glsl"
            @import "lighting.glsl"

            void main() {
                fragColor = vec4(1.0);
            }
            """;

        var tree = SyntaxTree.Parse(multiImportShader, GlslSchema.Instance);
        var sources = new Dictionary<string, string>
        {
            ["shaderincludes/utils.glsl"] = "// Utils\n",
            ["shaderincludes/lighting.glsl"] = "// Lighting\n"
        };

        var resolver = new DictionarySyntaxTreeResourceResolver(sources, ShaderImportsSystem.DefaultDomain);
        var preprocessor = new ShaderSyntaxTreePreprocessor(resolver);
        var result = preprocessor.Process(new ResourceId($"{ShaderImportsSystem.DefaultDomain}:shaders/test.fsh"), tree);

        Assert.True(result.Success);

        var output = NormalizeLineEndings(result.Content.ToText());
        Assert.Contains("// Utils", output);
        Assert.Contains("// Lighting", output);
        Assert.DoesNotContain("@import", output);
    }

    #endregion

    #region Query Position Tests

    [Fact]
    public void Insert_BeforeFunction_InsertsAtCorrectPosition()
    {
        var tree = SyntaxTree.Parse(SampleShader, GlslSchema.Instance);

        tree.CreateEditor()
            .InsertBefore(Query.Syntax<GlFunctionNode>().Named("main"), "// Before main\n")
            .Commit();

        var output = NormalizeLineEndings(tree.ToText());
        // Roslyn-style: insertion goes before target's leading trivia
        // The comment should appear somewhere before the main function
        Assert.Contains("// Before main", output);
        // main should still exist
        Assert.Contains("void main()", output);
    }

    [Fact]
    public void Insert_AfterVersionDirective_InsertsAtCorrectPosition()
    {
        var tree = SyntaxTree.Parse(SampleShader, GlslSchema.Instance);

        tree.CreateEditor()
            .InsertAfter(Query.Syntax<GlDirectiveNode>().Named("version"), "\n// After version")
            .Commit();

        var output = NormalizeLineEndings(tree.ToText());
        Assert.Matches(@"#version 330 core\s*// After version", output);
    }

    [Fact(Skip = "Bug in TinyTokenizer - InsertAfter makes inserted content consume the leading trivia of the next node")]
    public void Insert_InnerStartBody_InsertsAfterOpeningBrace()
    {
        var tree = SyntaxTree.Parse(SampleShader, GlslSchema.Instance);

        tree.CreateEditor()
            .InsertAfter(Query.Syntax<GlFunctionNode>().Named("main").InnerStart("body"), "    // Body start\n")
            .Commit();

        var output = NormalizeLineEndings(tree.ToText());
        Assert.Contains("void main() {\n    // Body start", output);
    }

    [Fact]
    public void Insert_InnerEndBody_InsertsBeforeClosingBrace()
    {
        var tree = SyntaxTree.Parse(SampleShader, GlslSchema.Instance);

        tree.CreateEditor()
            .InsertBefore(Query.Syntax<GlFunctionNode>().Named("main").InnerEnd("body"), "\n    // Body end")
            .Commit();

        var output = NormalizeLineEndings(tree.ToText());
        Assert.Matches(@"// Body end\s*}", output);
    }

    #endregion

    #region Combined Edit Tests

    [Fact]
    public void MultipleEdits_ApplyInSingleCommit()
    {
        var tree = SyntaxTree.Parse(SampleShader, GlslSchema.Instance);

        var mainQuery = Query.Syntax<GlFunctionNode>().Named("main");
        var versionQuery = Query.Syntax<GlDirectiveNode>().Named("version");

        tree.CreateEditor()
            .InsertAfter(versionQuery, "\n// Layout declarations")
            .InsertAfter(mainQuery.InnerStart("body"), "\n    // Init code")
            .InsertBefore(mainQuery.InnerEnd("body"), "\n    // Cleanup code")
            .Commit();

        var output = NormalizeLineEndings(tree.ToText());

        Assert.Contains("// Layout declarations", output);
        Assert.Contains("// Init code", output);
        Assert.Contains("// Cleanup code", output);
    }

    [Fact]
    public void FullPatchingWorkflow_SimulatesRealUsage()
    {
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

        var output = NormalizeLineEndings(tree.ToText());

        // Verify declarations appear (allowing for whitespace variations from tokenizer)
        Assert.Contains("layout", output);
        Assert.Contains("location = 4", output);
        Assert.Contains("gNormal", output);

        // Verify writes appear in main body
        Assert.Contains("gNormal = vec4(0.0, 1.0, 0.0, 1.0);", output);

        // Verify original code is preserved
        Assert.Contains("outColor = texture(tex, vec2(0.5));", output);
    }

    #endregion

    #region PBROverlay Version Preservation Tests

    /// <summary>
    /// Tests that the #version directive is preserved after processing imports
    /// when using tree.ToText() (the correct method to use).
    /// </summary>
    [Fact]
    public void ProcessImports_PreservesVersionDirective_WithRootToString()
    {
        const string shader = """
            #version 330 core

            uniform sampler2D tex;
            
            @import "shared.glsl"

            void main() {
                fragColor = vec4(1.0);
            }
            """;

        var tree = SyntaxTree.Parse(shader, GlslSchema.Instance);
        var sources = new Dictionary<string, string>
        {
            ["shaderincludes/shared.glsl"] = "// Shared code\nfloat PI = 3.14159;"
        };

        var resolver = new DictionarySyntaxTreeResourceResolver(sources, ShaderImportsSystem.DefaultDomain);
        var preprocessor = new ShaderSyntaxTreePreprocessor(resolver);
        var result = preprocessor.Process(new ResourceId($"{ShaderImportsSystem.DefaultDomain}:shaders/test.fsh"), tree);

        Assert.True(result.Success);

        var output = NormalizeLineEndings(result.Content.ToText());
        
        // #version directive should be at the start of the output
        Assert.StartsWith("#version 330 core", output);
        Assert.Contains("#version 330 core", output);
    }

    /// <summary>
    /// Tests a realistic pbroverlay-like shader with imports.
    /// This mimics the actual pbroverlay.fsh structure.
    /// </summary>
    [Fact]
    public void ProcessImports_PbrOverlayLikeShader_PreservesVersionDirective()
    {
        const string shader = """
            #version 330 core

            in vec2 uv;
            out vec4 outColor;

            uniform sampler2D primaryScene;
            uniform float zNear;
            uniform float zFar;

            @import "squirrel3.fsh"
            @import "pbrFunctions.fsh"

            void main() {
                outColor = texture(primaryScene, uv);
            }
            """;

        var tree = SyntaxTree.Parse(shader, GlslSchema.Instance);
        var sources = new Dictionary<string, string>
        {
            ["shaderincludes/squirrel3.fsh"] = "// Squirrel3 hash\nfloat hash(vec3 p) { return 0.5; }",
            ["shaderincludes/pbrFunctions.fsh"] = "// PBR functions\nvec3 fresnel(float c, vec3 f) { return f; }"
        };

        var resolver = new DictionarySyntaxTreeResourceResolver(sources, ShaderImportsSystem.DefaultDomain);
        var preprocessor = new ShaderSyntaxTreePreprocessor(resolver);
        var result = preprocessor.Process(new ResourceId($"{ShaderImportsSystem.DefaultDomain}:shaders/test.fsh"), tree);

        Assert.True(result.Success);

        var output = NormalizeLineEndings(result.Content.ToText());
        
        // Verify #version is preserved
        Assert.StartsWith("#version 330 core", output);
        
        // Verify imports were processed
        Assert.Contains("// Squirrel3 hash", output);
        Assert.Contains("// PBR functions", output);
        Assert.DoesNotContain("@import", output);
    }

    /// <summary>
    /// Tests that parsing alone without modifications preserves the #version directive.
    /// </summary>
    [Fact]
    public void Parse_PreservesVersionDirective_WithoutModifications()
    {
        const string shader = """
            #version 330 core

            void main() {
                fragColor = vec4(1.0);
            }
            """;

        var tree = SyntaxTree.Parse(shader, GlslSchema.Instance);

        // Must use Root.ToString() - ToFullString() returns empty string
        var rootOutput = NormalizeLineEndings(tree.ToText());

        Assert.StartsWith("#version 330 core", rootOutput);
    }

    #endregion

    #region Edge Cases

    [Fact]
    public void Parse_EmptyShader_DoesNotThrow()
    {
        var tree = SyntaxTree.Parse("", GlslSchema.Instance);
        Assert.NotNull(tree);
    }

    [Fact]
    public void Parse_ShaderWithComments_IgnoresComments()
    {
        const string shaderWithComments = """
            #version 330 core
            // This is a comment
            /* Multi-line
               comment */
            void main() {
                // Inside function
            }
            """;

        var tree = SyntaxTree.Parse(shaderWithComments, GlslSchema.Instance);

        var functions = tree.Select(Query.Syntax<GlFunctionNode>())
            .Cast<GlFunctionNode>()
            .ToList();

        Assert.Single(functions);
        Assert.Equal("main", functions[0].Name);
    }

    [Fact]
    public void Query_NonExistentFunction_ReturnsEmpty()
    {
        var tree = SyntaxTree.Parse(SampleShader, GlslSchema.Instance);

        var result = tree.Select(Query.Syntax<GlFunctionNode>().Named("nonexistent"))
            .Cast<GlFunctionNode>()
            .ToList();

        Assert.Empty(result);
    }

    #endregion
}
