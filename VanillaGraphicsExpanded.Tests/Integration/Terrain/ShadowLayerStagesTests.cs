using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.PBR.Tessellation;
using VanillaGraphicsExpanded.Rendering.Shadows.Geometry;
using VanillaGraphicsExpanded.Tests.Integration.Support.Rendering.Shaders;
using VanillaGraphicsExpanded.Tests.Support.GPU.Harness;
using VanillaGraphicsExpanded.Tests.Support.GPU.Native;
using VanillaGraphicsExpanded.Tests.Support.Rendering.Shaders;
using VanillaGraphicsExpanded.Tests.Support.Terrain;
using Xunit;

namespace VanillaGraphicsExpanded.Tests.Integration.Terrain;

/// <summary>Compiles page routing with installed ordinary and shader-storage terrain shadow inputs on the GL 4.5 path.</summary>
[Collection("GPU")]
[Trait("Category", "GpuIntegration")]
public sealed class ShadowLayerStagesTests(HeadlessGLFixture fixture) : RenderTestBase(fixture)
{
    #region Public API
    /// <summary>Final terrain displacement, page metadata and alpha-tested materials form one linkable stage chain.</summary>
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public void InstalledTerrainShadowStagesLinkThroughPageRouting(int ssbo)
    {
        // Arrange: Resolve the installed shadow shader using the same source path as existing terrain tests.
        EnsureContextValid();
        using var shaders = new TerrainShaderTestFixture();
        string sourceVertex = InstalledEngineShaderSources.Build("chunkshadowmap.vsh", 2, 0, 0, ssbo, 0);
        string sourceFragment = InstalledEngineShaderSources.Build("chunkshadowmap.fsh", 2, 0, 0, ssbo, 0);
        var routed = ShadowLayerStages.Prepare(sourceVertex, sourceFragment);
        var tessellation = TerrainTessellationTestAssets.Generate(routed.Vertex);
        var stageSources = TerrainTessellationStages.Describe(tessellation, TerrainTessellationPatches.EnabledDefine);

        // Act: Compile ordinary source plus generated TCS/TES and OpenGL 4.5 geometry layer routing.
        int vertex = shaders.Compile(ShaderType.VertexShader, routed.Vertex);
        int control = shaders.Compile(ShaderType.TessControlShader, stageSources.Control.Source!);
        int evaluation = shaders.Compile(ShaderType.TessEvaluationShader, stageSources.Evaluation.Source!);
        int geometry = shaders.Compile(ShaderType.GeometryShader, routed.Geometry);
        int fragment = shaders.Compile(ShaderType.FragmentShader, routed.Fragment);
        using var program = NativeResourceSetup.CreateProgram();
        GL.AttachShader(program.ProgramId, vertex);
        GL.AttachShader(program.ProgramId, control);
        GL.AttachShader(program.ProgramId, evaluation);
        GL.AttachShader(program.ProgramId, geometry);
        GL.AttachShader(program.ProgramId, fragment);
        bool linked = NativeProgramFixture.Link(program, out var error);

        // Assert: Both terrain layouts retain their native shadow material interface through TES and gl_Layer output.
        Assert.True(linked, error);
        Assert.Contains("applyVertexWarping", routed.Vertex);
        Assert.Contains("applyGlobalWarping", routed.Vertex);
        Assert.Contains("vge_pageReference", routed.Vertex);
        Assert.Contains("flat out uint vge_pageIndex", tessellation.Control);
        Assert.Contains("flat out uint vge_pageIndex", tessellation.Evaluation);
        Assert.Contains("gl_Layer = int(shadowRasterPages[page].destination.x)", routed.Geometry);
        Assert.Contains("if (outColor.a < 0.02) discard;", routed.Fragment);
        Assert.Equal(ErrorCode.NoError, GL.GetError());
    }
    #endregion
}
