using TinyTokenizer.Ast;
using VanillaGraphicsExpanded.PBR.Tessellation;
using VanillaGraphicsExpanded.Rendering.Shaders;
using VanillaGraphicsExpanded.Rendering.Shadows.Geometry;
using VanillaGraphicsExpanded.Tests.Integration.Support.Rendering.Shaders;
using VanillaGraphicsExpanded.Tests.Support.GPU.Harness;
using VanillaGraphicsExpanded.Tests.Support.GPU.Shaders;
using VanillaGraphicsExpanded.Tests.Support.Terrain;
using Xunit;

namespace VanillaGraphicsExpanded.Tests.Integration.Terrain;

/// <summary>Checks virtual-view generation around installed terrain materials and tessellation sources.</summary>
[Collection("GPU")]
[Trait("Category", "GpuIntegration")]
public sealed class ShadowVirtualStagesTests(HeadlessGLFixture fixture) : RenderTestBase(fixture)
{
    #region Public API
    /// <summary>Selectors propagate through generated tessellation and final clipping wraps deformed material output.</summary>
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public void TerrainVirtualStagesPreserveMaterialAndFinalProducerOrdering(int ssbo)
    {
        // Arrange: use both installed chunk-shadow vertex layouts and the production tessellation templates.
        EnsureContextValid();
        using var assets = new BinaryShaderApiFixture();
        string nativeVertex = InstalledEngineShaderSources.Build("chunkshadowmap.vsh", 2, 0, 0, ssbo, 0);
        string nativeFragment = InstalledEngineShaderSources.Build("chunkshadowmap.fsh", 2, 0, 0, ssbo, 0);
        string selectedVertex = ShadowVirtualStages.SelectViews(nativeVertex);
        var tessellation = TerrainTessellationTestAssets.Generate(selectedVertex);
        string vertexInterface = ShaderSourceCode.Load(assets.Api, "includes/shadows/vertex-interface", "glsl").EmittedSource;
        string fragmentInterface = ShaderSourceCode.Load(assets.Api, "includes/shadows/fragment-interface", "glsl").EmittedSource;
        string viewFunctions = ShaderSourceCode.Load(assets.Api, "includes/shadows/views", "glsl").EmittedSource;
        string writeFunctions = ShaderSourceCode.Load(assets.Api, "includes/shadows/writing", "glsl").EmittedSource;
        string finalPosition = SyntaxTree.Parse(tessellation.Evaluation, GlslSchema.Instance)
            .Select(Query.Syntax<GlStageIoNode>())
            .Cast<GlStageIoNode>()
            .Any(static output => output.Storage == GlInterfaceStorage.Output && output.Name == "vge_shadowPosition")
            ? "vge_shadowPosition" : "worldPos";

        // Act: generate the final evaluation producer only after material outputs have crossed tessellation.
        string finalEvaluation = ShadowVirtualStages.FinalProducer(
            InsertStageDeclarations(tessellation.Evaluation, vertexInterface + "\n" + viewFunctions), finalPosition);
        string finalFragment = ShadowVirtualStages.Fragment(
            InsertStageDeclarations(nativeFragment, string.Join("\n", fragmentInterface, viewFunctions, writeFunctions)));

        // Assert: selectors propagate through TCS/TES and four clip outputs follow the final material position.
        Assert.Contains("layout(location=10) in uvec2 vge_shadowSelection;", selectedVertex);
        Assert.Contains("flat out uint vge_shadowObject;", selectedVertex);
        Assert.Contains("flat out uint vge_shadowView;", selectedVertex);
        Assert.Contains("flat in uint vge_shadowObject[];", tessellation.Control);
        Assert.Contains("flat out uint tc_vge_shadowView[];", tessellation.Control);
        Assert.Contains("flat in uint tc_vge_shadowView[];", tessellation.Evaluation);
        Assert.Contains("flat out uint vge_shadowView;", tessellation.Evaluation);
        Assert.Contains("out float gl_ClipDistance[4];", finalEvaluation);
        Assert.Contains($"gl_Position=shadowProject(vge_shadowView,{finalPosition});", finalEvaluation);
        Assert.Contains("for(int plane=0;plane<4;plane++) gl_ClipDistance[plane]=distances[plane];", finalEvaluation);
        Assert.Contains("if (offset != 0.0)", finalEvaluation);
        Assert.DoesNotContain("gl_Layer", finalEvaluation);
        Assert.DoesNotContain("layout(points)", finalEvaluation);

        // Material discard remains inside its original body; guarded atomic publication executes after it returns.
        int discard = finalFragment.IndexOf("discard;", StringComparison.Ordinal);
        int materialCall = finalFragment.IndexOf("vgeShadowMaterialMain();", StringComparison.Ordinal);
        int depthRead = finalFragment.IndexOf("float depth=gl_FragCoord.z;", StringComparison.Ordinal);
        int directionalClamp = finalFragment.IndexOf(
            "if(submission.z==0u && !isnan(depth) && !isinf(depth)) depth=clamp(depth,0.0,1.0);", StringComparison.Ordinal);
        int atomicWrite = finalFragment.LastIndexOf(
            "shadowAtomicDepth(view,origin+ivec2(gl_FragCoord.xy),depth);", StringComparison.Ordinal);
        Assert.True(discard >= 0);
        Assert.True(materialCall >= 0 && atomicWrite > materialCall);
        Assert.True(depthRead > materialCall && directionalClamp > depthRead && atomicWrite > directionalClamp);
        Assert.Contains("flat in uint vge_shadowView;", finalFragment);
        Assert.DoesNotContain("gl_Layer", finalFragment);
    }

    /// <summary>Virtual producer generation rejects existing clip/cull owners and redeclared output blocks.</summary>
    [Theory]
    [InlineData("#version 450 core\nout float gl_ClipDistance[4];\nvoid main(){ gl_Position=vec4(0); gl_ClipDistance[0]=1; }")]
    [InlineData("#version 450 core\nout float gl_ClipDistance[4];\nvoid main(){ gl_Position=vec4(0); gl_CullDistance[0]=1; }")]
    [InlineData("#version 450 core\nout float gl_ClipDistance[4];\nout gl_PerVertex { vec4 gl_Position; }; void main(){ gl_Position=vec4(0); }")]
    public void FinalProducerRejectsUnsupportedBuiltInOutputDeclarations(string source)
    {
        // Arrange: isolate each unsupported built-in output ownership form in a tiny parsed stage.

        // Act and assert: a second clip/cull owner cannot silently collide with the generated producer.
        Assert.Throws<System.NotSupportedException>(() => ShadowVirtualStages.FinalProducer(source, "worldPos"));
    }

    /// <summary>Accepts exactly one typed four-plane output and ignores declaration-shaped comments.</summary>
    [Fact]
    public void FinalProducerValidatesClipDeclarationAsTypedArraySyntax()
    {
        // Arrange: comment text mentions a competing declaration while one valid output owns the interface.
        const string source = """
            #version 450 core
            // out float gl_ClipDistance[3]; out float gl_CullDistance[4];
            out float gl_ClipDistance[4];
            void main() { gl_Position=vec4(0.0); }
            """;

        // Act
        string generated = ShadowVirtualStages.FinalProducer(source, "worldPos");

        // Assert: the declaration survives and the generated wrapper writes all four planes.
        Assert.Contains("out float gl_ClipDistance[4];", generated);
        Assert.Contains("for(int plane=0;plane<4;plane++) gl_ClipDistance[plane]=distances[plane];", generated);
        Assert.Contains("// out float gl_ClipDistance[3]; out float gl_CullDistance[4];", generated);
    }

    /// <summary>Rejects clip declarations whose typed direction, element type, or literal array extent is unsupported.</summary>
    [Theory]
    [InlineData("out float gl_ClipDistance;")]
    [InlineData("out float gl_ClipDistance[3];")]
    [InlineData("out float gl_ClipDistance[4][2];")]
    [InlineData("in float gl_ClipDistance[4];")]
    [InlineData("out vec4 gl_ClipDistance[4];")]
    [InlineData("// out float gl_ClipDistance[4];")]
    public void FinalProducerRejectsInvalidClipArrayDeclarations(string declaration)
    {
        // Arrange
        string source = $"#version 450 core\n{declaration}\nvoid main() {{ gl_Position=vec4(0.0); }}";

        // Act and assert
        Assert.Throws<System.NotSupportedException>(() => ShadowVirtualStages.FinalProducer(source, "worldPos"));
    }

    /// <summary>Geometry emission cannot be wrapped while guaranteeing a final clipping write for every emitted vertex.</summary>
    [Theory]
    [InlineData("EmitVertex();")]
    [InlineData("EmitStreamVertex(0);")]
    public void FinalProducerRejectsGeometryEmission(string emission)
    {
        // Arrange: use a valid point-output geometry entry point with one emitted vertex.
        string source = $"#version 450 core\nout float gl_ClipDistance[4];\nlayout(points) in; layout(points,max_vertices=1) out; void main() {{ gl_Position=gl_in[0].gl_Position; {emission} EndPrimitive(); }}";

        // Act and assert: emission behavior needs a dedicated producer that owns every output path.
        Assert.Throws<System.NotSupportedException>(() => ShadowVirtualStages.FinalProducer(source, "worldPos"));
    }

    /// <summary>Rejects a geometry-emission identifier used through a macro alias.</summary>
    [Fact]
    public void FinalProducerRejectsMacroAliasedGeometryEmission()
    {
        // Arrange: the parsed call names the built-in after preprocessing applies the macro alias.
        const string source = """
            #version 450 core
            out float gl_ClipDistance[4];
            #define VGE_EMIT EmitVertex
            void main() { VGE_EMIT(); }
            """;

        // Act and assert: emission remains outside the supported wrapped producer contract.
        Assert.Throws<System.NotSupportedException>(() => ShadowVirtualStages.FinalProducer(source, "worldPos"));
    }

    /// <summary>Fragment depth overrides and early test ordering require separately qualified shadow writers.</summary>
    [Theory]
    [InlineData("#version 450 core\nvoid main(){ gl_FragDepth=0.5; }")]
    [InlineData("#version 450 core\nlayout(early_fragment_tests) in; void main(){}")]
    public void FragmentWriterRejectsDepthOrEarlyTestOverrides(string source)
    {
        // Arrange: isolate each fragment behavior that could bypass or reorder the atomic depth value.

        // Act and assert: the generator requires an explicitly qualified variant for either behavior.
        Assert.Throws<System.NotSupportedException>(() => ShadowVirtualStages.Fragment(source));
    }

    /// <summary>Ignores comments that resemble early-test layout declarations while preserving their trivia.</summary>
    [Fact]
    public void FragmentWriterIgnoresCommentedEarlyTestLayout()
    {
        // Arrange
        const string source = """
            #version 450 core
            // layout(early_fragment_tests) in;
            /* layout(early_fragment_tests) in; */
            void main() { }
            // trailing comment
            """;

        // Act
        string generated = ShadowVirtualStages.Fragment(source);

        // Assert
        Assert.Contains("// layout(early_fragment_tests) in;", generated);
        Assert.Contains("/* layout(early_fragment_tests) in; */", generated);
        Assert.Contains("// trailing comment", generated);
        Assert.Contains("shadowAtomicDepth", generated);
    }
    #endregion

    #region Private
    /// <summary>Inserts explicit stage declarations before main, after any generated preamble and helper functions.</summary>
    private static string InsertStageDeclarations(string source, string declarations)
    {
        var tree = SyntaxTree.Parse(source, GlslSchema.Instance);
        tree.CreateEditor().InsertBefore(Query.Syntax<GlFunctionNode>().Named("main"), declarations + "\n").Commit();
        return tree.ToText();
    }
    #endregion
}
