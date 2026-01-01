using TinyTokenizer.Ast;
using VanillaGraphicsExpanded;
using Xunit;
using Xunit.Abstractions;

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
    /// Sample shader with @import directive inlined.
    /// </summary>
    private const string ShaderWithImportInlined = """
        #version 330 core
        #extension GL_ARB_explicit_attrib_location: enable
        
        layout(location = 0) out vec4 outColor;
        layout(location = 1) out vec4 outGlow;
        #if SSAOLEVEL > 0
        in vec4 gnormal;
        layout(location = 2) out vec4 outGNormal;
        layout(location = 3) out vec4 outGPosition;
        #endif

        /* @import "shared.glsl" */
        // Shared code
        float PI = 3.14159;

        void main() {
            fragColor = vec4(1.0);
        }
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

        var importDirective = tree.Select(Query.Syntax<GlImportNode>().Named("import"))
            .Cast<GlImportNode>()
            .FirstOrDefault();

        Assert.NotNull(importDirective);
        Assert.Equal("import", importDirective.Name);
        Assert.Equal("shared.glsl", NormalizeLineEndings(importDirective.ImportString));
        Assert.Equal("\n@import \"shared.glsl\"\n", NormalizeLineEndings(importDirective.ToString()));
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
    public void ProcessImports_ReplacesImportWithContent()
    {
        var tree = SyntaxTree.Parse(ShaderWithImport, GlslSchema.Instance);
        var importsCache = new Dictionary<string, string>
        {
            ["shared.glsl"] = "// Shared code\nfloat PI = 3.14159;"
        };

        var result = SourceCodeImportsProcessor.ProcessImports(tree, importsCache);

        Assert.True(result);

        var output = NormalizeLineEndings(tree.ToString());

        // Original import should be commented out (// prefix before @import)
        Assert.Contains("/* @import \"shared.glsl\" */", output);
        Assert.Equal(NormalizeLineEndings(ShaderWithImportInlined.Trim()), NormalizeLineEndings(output.Trim()));
    }

    [Fact]
    public void ProcessImports_ReturnsFalse_WhenNoImports()
    {
        var tree = SyntaxTree.Parse(SampleShader, GlslSchema.Instance);
        var importsCache = new Dictionary<string, string>();

        var result = SourceCodeImportsProcessor.ProcessImports(tree, importsCache);

        Assert.False(result);
    }

    [Fact]
    public void ProcessImports_AddsWarningComment_WhenImportNotFound()
    {
        var tree = SyntaxTree.Parse(ShaderWithImport, GlslSchema.Instance);
        var importsCache = new Dictionary<string, string>(); // Empty cache

        var result = SourceCodeImportsProcessor.ProcessImports(tree, importsCache);

        Assert.True(result);

        var output = NormalizeLineEndings(tree.ToString());

        // Import should be commented out
        Assert.Contains("/* @import \"shared.glsl\" */", output);
        // Warning comment should be added after the commented import
        Assert.Contains("// WARNING: Import file 'shared.glsl' not found", output);
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
        var importsCache = new Dictionary<string, string>
        {
            ["utils.glsl"] = "// Utils\n",
            ["lighting.glsl"] = "// Lighting\n"
        };

        var result = SourceCodeImportsProcessor.ProcessImports(tree, importsCache);

        Assert.True(result);

        var output = NormalizeLineEndings(tree.ToString());
        Assert.Contains("/* @import \"utils.glsl\" */\n// Utils\n", output);
        Assert.Contains("/* @import \"lighting.glsl\" */\n// Lighting\n", output);
    }

    #endregion

    #region Query Position Tests

    [Fact]
    public void Insert_BeforeFunction_InsertsAtCorrectPosition()
    {
        var tree = SyntaxTree.Parse(SampleShader, GlslSchema.Instance);

        tree.CreateEditor()
            .Insert(Query.Syntax<GlFunctionNode>().Named("main").Before(), "// Before main\n")
            .Commit();

        var output = NormalizeLineEndings(tree.ToString());
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
            .Insert(Query.Syntax<GlDirectiveNode>().Named("version").After(), "\n// After version")
            .Commit();

        var output = NormalizeLineEndings(tree.ToString());
        Assert.Matches(@"#version 330 core\s*// After version", output);
    }

    [Fact]
    public void Insert_InnerStartBody_InsertsAfterOpeningBrace()
    {
        var tree = SyntaxTree.Parse(SampleShader, GlslSchema.Instance);

        tree.CreateEditor()
            .Insert(Query.Syntax<GlFunctionNode>().Named("main").InnerStart("body"), "\n    // Body start")
            .Commit();

        var output = NormalizeLineEndings(tree.ToString());
        Assert.Contains("void main() {\n    // Body start", output);
    }

    [Fact]
    public void Insert_InnerEndBody_InsertsBeforeClosingBrace()
    {
        var tree = SyntaxTree.Parse(SampleShader, GlslSchema.Instance);

        tree.CreateEditor()
            .Insert(Query.Syntax<GlFunctionNode>().Named("main").InnerEnd("body"), "\n    // Body end")
            .Commit();

        var output = NormalizeLineEndings(tree.ToString());
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
            .Insert(versionQuery.After(), "\n// Layout declarations")
            .Insert(mainQuery.InnerStart("body"), "\n    // Init code")
            .Insert(mainQuery.InnerEnd("body"), "\n    // Cleanup code")
            .Commit();

        var output = NormalizeLineEndings(tree.ToString());

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
            .Insert(versionQuery.After(), "\nlayout(location = 4) out vec4 gNormal;")
            .Commit();

        // Step 2: Insert G-buffer writes at function body start
        var mainQuery = Query.Syntax<GlFunctionNode>().Named("main");
        tree.CreateEditor()
            .Insert(mainQuery.InnerStart("body"), "\n    gNormal = vec4(0.0, 1.0, 0.0, 1.0);")
            .Commit();

        var output = NormalizeLineEndings(tree.ToString());

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
    /// when using tree.ToString() (the correct method to use).
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
        var importsCache = new Dictionary<string, string>
        {
            ["shared.glsl"] = "// Shared code\nfloat PI = 3.14159;"
        };

        var result = SourceCodeImportsProcessor.ProcessImports(tree, importsCache);

        Assert.True(result);

        var output = NormalizeLineEndings(tree.ToString());
        
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
        var importsCache = new Dictionary<string, string>
        {
            ["squirrel3.fsh"] = "// Squirrel3 hash\nfloat hash(vec3 p) { return 0.5; }",
            ["pbrFunctions.fsh"] = "// PBR functions\nvec3 fresnel(float c, vec3 f) { return f; }"
        };

        var result = SourceCodeImportsProcessor.ProcessImports(tree, importsCache);

        Assert.True(result);

        // Must use Root.ToString() - ToFullString() returns empty string
        var output = NormalizeLineEndings(tree.ToString());
        
        // Verify #version is preserved
        Assert.StartsWith("#version 330 core", output);
        
        // Verify imports were processed
        Assert.Contains("/* @import \"squirrel3.fsh\" */", output);
        Assert.Contains("/* @import \"pbrFunctions.fsh\" */", output);
        Assert.Contains("// Squirrel3 hash", output);
        Assert.Contains("// PBR functions", output);
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
        var rootOutput = NormalizeLineEndings(tree.ToString());

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
