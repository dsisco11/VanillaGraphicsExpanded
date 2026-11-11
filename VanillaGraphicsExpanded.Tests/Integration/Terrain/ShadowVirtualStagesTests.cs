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
        string viewFunctions = ShaderSourceCode.Load(assets.Api, "includes/shadows/views", "glsl").EmittedSource;
        string writeFunctions = ShaderSourceCode.Load(assets.Api, "includes/shadows/writing", "glsl").EmittedSource;
        string finalPosition = tessellation.Evaluation.Contains("vge_shadowPosition", StringComparison.Ordinal)
            ? "vge_shadowPosition" : "worldPos";

        // Act: generate the final evaluation producer only after material outputs have crossed tessellation.
        string finalEvaluation = ShadowVirtualStages.FinalProducer(tessellation.Evaluation, finalPosition, viewFunctions);
        string finalFragment = ShadowVirtualStages.Fragment(nativeFragment, viewFunctions, writeFunctions);

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
        int atomicWrite = finalFragment.LastIndexOf("shadowAtomicDepth(view,origin+ivec2(gl_FragCoord.xy),gl_FragCoord.z);", StringComparison.Ordinal);
        Assert.True(discard >= 0);
        Assert.True(materialCall >= 0 && atomicWrite > materialCall);
        Assert.Contains("flat in uint vge_shadowView;", finalFragment);
        Assert.DoesNotContain("gl_Layer", finalFragment);
    }

    /// <summary>Virtual producer generation rejects existing clip/cull owners and redeclared output blocks.</summary>
    [Theory]
    [InlineData("#version 450 core\nvoid main(){ gl_Position=vec4(0); gl_ClipDistance[0]=1; }")]
    [InlineData("#version 450 core\nvoid main(){ gl_Position=vec4(0); gl_CullDistance[0]=1; }")]
    [InlineData("#version 450 core\nout gl_PerVertex { vec4 gl_Position; }; void main(){ gl_Position=vec4(0); }")]
    public void FinalProducerRejectsUnsupportedBuiltInOutputDeclarations(string source)
    {
        // Arrange: isolate each unsupported built-in output ownership form in a tiny parsed stage.

        // Act and assert: a second clip/cull owner cannot silently collide with the generated producer.
        Assert.Throws<System.NotSupportedException>(() => ShadowVirtualStages.FinalProducer(source, "worldPos", string.Empty));
    }

    /// <summary>Geometry emission cannot be wrapped while guaranteeing a final clipping write for every emitted vertex.</summary>
    [Theory]
    [InlineData("EmitVertex();")]
    [InlineData("EmitStreamVertex(0);")]
    public void FinalProducerRejectsGeometryEmission(string emission)
    {
        // Arrange: use a valid point-output geometry entry point with one emitted vertex.
        string source = $"#version 450 core\nlayout(points) in; layout(points,max_vertices=1) out; void main() {{ gl_Position=gl_in[0].gl_Position; {emission} EndPrimitive(); }}";

        // Act and assert: emission behavior needs a dedicated producer that owns every output path.
        Assert.Throws<System.NotSupportedException>(() => ShadowVirtualStages.FinalProducer(source, "worldPos", string.Empty));
    }

    /// <summary>Fragment depth overrides and early test ordering require separately qualified shadow writers.</summary>
    [Theory]
    [InlineData("#version 450 core\nvoid main(){ gl_FragDepth=0.5; }")]
    [InlineData("#version 450 core\nlayout(early_fragment_tests) in; void main(){}")]
    public void FragmentWriterRejectsDepthOrEarlyTestOverrides(string source)
    {
        // Arrange: isolate each fragment behavior that could bypass or reorder the atomic depth value.

        // Act and assert: the generator requires an explicitly qualified variant for either behavior.
        Assert.Throws<System.NotSupportedException>(() => ShadowVirtualStages.Fragment(source, string.Empty, string.Empty));
    }
    #endregion
}
