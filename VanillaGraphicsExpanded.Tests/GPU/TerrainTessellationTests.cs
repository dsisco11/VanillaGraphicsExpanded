using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.ModSystems;
using Vintagestory.Client.NoObf;
using VanillaGraphicsExpanded.PBR.Tessellation;
using VanillaGraphicsExpanded.Tests.GPU.Fixtures;
using VanillaGraphicsExpanded.Tests.GPU.Helpers;

namespace VanillaGraphicsExpanded.Tests.GPU;

/// <summary>Validates identity tessellation against installed terrain interfaces and ordinary rasterization.</summary>
[Collection("GPU")]
[Trait("Category", "GPU")]
public sealed class TerrainTessellationTests : RenderTestBase
{
    /// <summary>Uses the shared headless context.</summary>
    public TerrainTessellationTests(HeadlessGLFixture fixture) : base(fixture) { }

    #region Installed interfaces
    /// <summary>Exercises both terrain families, SSBO layouts, SSAO outputs and both shadow cascades.</summary>
    [Theory]
    [InlineData("chunkopaque", 0, 0)]
    [InlineData("chunkopaque", 1, 1)]
    [InlineData("chunktopsoil", 0, 0)]
    [InlineData("chunktopsoil", 1, 1)]
    public void InstalledTerrainInterfaceLinks(string family, int ssbo, int ssao)
    {
        EnsureContextValid();
        int vertex = Compile(ShaderType.VertexShader, PbrSurfaceInstalledShaderTests.Build(family + ".vsh", 2, 0, ssao, ssbo, 0));
        int fragment = Compile(ShaderType.FragmentShader, PbrSurfaceInstalledShaderTests.Build(family + ".fsh", 2, 0, ssao, ssbo, 0));
        try
        {
            var sources = TerrainTessellationStages.Generate(Source(vertex));
            Assert.True(TerrainTessellationLinker.TryCreate(vertex, fragment, sources, TerrainTessellationPatches.EnabledDefine, 4, out int program, out string error), error);
            GL.DeleteProgram(program);
        }
        finally { GL.DeleteShader(vertex); GL.DeleteShader(fragment); }
    }
    /// <summary>Installing a tessellated executable preserves engine stage objects and scopes topology to its managed owner.</summary>
    [Fact]
    public void EngineProgramInstallationPreservesStageOwnership()
    {
        EnsureContextValid();
        int vertex = Compile(ShaderType.VertexShader, PbrSurfaceInstalledShaderTests.Build("chunkopaque.vsh", 2, 0, 0, 0, 0));
        int fragment = Compile(ShaderType.FragmentShader, PbrSurfaceInstalledShaderTests.Build("chunkopaque.fsh", 2, 0, 0, 0, 0));
        int ordinary = Link(vertex, fragment);
        var vertexObject = new Shader { ShaderId = vertex, Code = Source(vertex) };
        var fragmentObject = new Shader { ShaderId = fragment };
        var owner = new ShaderProgram { PassName = "chunkopaque", AssetDomain = "game", ProgramId = ordinary, VertexShader = vertexObject, FragmentShader = fragmentObject };
        int previousLevel = ConfigModSystem.Config.MaterialAtlas.UndisplacedTessellationLevel;
        var previousProgram = ShaderProgramBase.CurrentShaderProgram;
        bool previousHook = TerrainTessellationPrograms.DrawHookAvailable;
        var previousLog = TerrainTessellationPrograms.Log;
        try
        {
            ConfigModSystem.Config.MaterialAtlas.UndisplacedTessellationLevel = 4;
            TerrainTessellationPrograms.DrawHookAvailable = true;
            string diagnostic = "";
            TerrainTessellationPrograms.Log = message => diagnostic = message;
            TerrainTessellationPatches.Prepare(owner);
            TerrainTessellationPatches.Configure(owner);
            TerrainTessellationPrograms.Prepare(owner);
            Assert.True(ordinary != owner.ProgramId, diagnostic);
            Assert.False(GL.IsProgram(ordinary));
            Assert.Same(vertexObject, owner.VertexShader); Assert.Same(fragmentObject, owner.FragmentShader);
            Assert.Equal("chunkopaque", owner.PassName);
            Assert.True(GL.IsShader(vertex)); Assert.True(GL.IsShader(fragment));
            ShaderProgramBase.CurrentShaderProgram = owner;
            Assert.Equal(PrimitiveType.Patches, TerrainTessellationPrograms.Topology(PrimitiveType.Triangles));
            Assert.Equal(PrimitiveType.Lines, TerrainTessellationPrograms.Topology(PrimitiveType.Lines));
            var cache = VanillaGraphicsExpanded.Rendering.GlStateCache.Current;
            int previousPatchSize = cache.PatchVertices;
            cache.SetPatchVertices(5);
            VanillaGraphicsExpanded.HarmonyPatches.TerrainTessellationDrawHook.Prefix(out int? saved);
            Assert.Equal(3, GL.GetInteger(GetPName.PatchVertices));
            VanillaGraphicsExpanded.HarmonyPatches.TerrainTessellationDrawHook.Finalizer(saved);
            Assert.Equal(5, GL.GetInteger(GetPName.PatchVertices));
            cache.SetPatchVertices(previousPatchSize);
            ShaderProgramBase.CurrentShaderProgram = new ShaderProgram { ProgramId = owner.ProgramId };
            Assert.Equal(PrimitiveType.Triangles, TerrainTessellationPrograms.Topology(PrimitiveType.Triangles));
            ShaderProgramBase.CurrentShaderProgram = owner;
            TerrainTessellationPrograms.Forget(owner);
            Assert.Equal(PrimitiveType.Triangles, TerrainTessellationPrograms.Topology(PrimitiveType.Triangles));
        }
        finally
        {
            TerrainTessellationPrograms.Forget(owner);
            ShaderProgramBase.CurrentShaderProgram = previousProgram;
            ConfigModSystem.Config.MaterialAtlas.UndisplacedTessellationLevel = previousLevel;
            TerrainTessellationPrograms.DrawHookAvailable = previousHook;
            TerrainTessellationPrograms.Log = previousLog;
            GL.DeleteProgram(owner.ProgramId); GL.DeleteShader(vertex); GL.DeleteShader(fragment);
        }
    }
    #endregion

    #region Raster equivalence
    /// <summary>Interpolated colors and last-vertex flat data survive subdivision with nonuniform clip W.</summary>
    [Theory]
    [InlineData(1)]
    [InlineData(4)]
    public void UndisplacedRasterMatchesTriangles(int level)
    {
        EnsureContextValid();
        int vertex = Compile(ShaderType.VertexShader, """
            #version 430 core
            out vec4 rgba;
            out vec2 uv;
            flat out int renderFlags;
            void main() {
                vec2 p[3] = vec2[3](vec2(-1,-1), vec2(3,-1), vec2(-1,3));
                float w = float(gl_VertexID + 1);
                gl_Position = vec4(p[gl_VertexID] * w, (float(gl_VertexID) * 0.2 - 0.2) * w, w);
                uv = p[gl_VertexID] * 0.25 + 0.25;
                rgba = vec4(float(gl_VertexID == 0), float(gl_VertexID == 1), float(gl_VertexID == 2), 1);
                renderFlags = gl_VertexID + 1;
            }
            """);
        int fragment = Compile(ShaderType.FragmentShader, """
            #version 430 core
            in vec4 rgba;
            in vec2 uv;
            flat in int renderFlags;
            layout(location=0) out vec4 color;
            layout(location=1) out vec4 coordinates;
            void main() { color = vec4(rgba.rgb, float(renderFlags)); coordinates = vec4(uv, gl_FragCoord.z, 1); }
            """);
        int ordinary = 0, tessellated = 0, vao = GL.GenVertexArray();
        GL.GetInteger(GetPName.PatchVertices, out int previousPatchVertices);
        try
        {
            ordinary = Link(vertex, fragment);
            Assert.True(TerrainTessellationLinker.TryCreate(vertex, fragment, TerrainTessellationStages.Generate(Source(vertex)), TerrainTessellationPatches.EnabledDefine, level, out tessellated, out string error), error);
            using var framework = new ShaderTestFramework();
            using var target = framework.CreateTestGBuffer(16, 16, PixelInternalFormat.Rgba32f, 2);
            target.BindWithViewport();
            GL.Disable(EnableCap.DepthTest); GL.Disable(EnableCap.Blend); GL.Disable(EnableCap.CullFace);
            GL.BindVertexArray(vao);
            GL.UseProgram(ordinary); GL.DrawArrays(PrimitiveType.Triangles, 0, 3);
            var expected = target[0].ReadPixels();
            var expectedCoordinates = target[1].ReadPixels();
            GL.PatchParameter(PatchParameterInt.PatchVertices, 3);
            GL.UseProgram(tessellated); GL.DrawArrays(PrimitiveType.Patches, 0, 3);
            var actual = target[0].ReadPixels();
            var actualCoordinates = target[1].ReadPixels();
            for (int i = 0; i < actualCoordinates.Length; i++) Assert.InRange(actualCoordinates[i], expectedCoordinates[i] - .0001f, expectedCoordinates[i] + .0001f);
            Assert.Equal(expected.Length, actual.Length);
            for (int i = 0; i < actual.Length; i++) Assert.InRange(actual[i], expected[i] - .0001f, expected[i] + .0001f);
            Assert.Equal(3f, actual[3]);
        }
        finally
        {
            GL.UseProgram(0); GL.BindVertexArray(0); GL.PatchParameter(PatchParameterInt.PatchVertices, previousPatchVertices);
            GL.DeleteVertexArray(vao); if (ordinary != 0) GL.DeleteProgram(ordinary); if (tessellated != 0) GL.DeleteProgram(tessellated);
            GL.DeleteShader(vertex); GL.DeleteShader(fragment);
        }
    }
    #endregion

    #region Driver setup
    /// <summary>Returns fixture source for metadata preparation without an inspection executable.</summary>
    internal static string Source(int shader)
    {
        GL.GetShader(shader, ShaderParameter.ShaderSourceLength, out int length);
        GL.GetShaderSource(shader, length, out _, out string source);
        return source;
    }
    /// <summary>Compiles a runtime GLSL stage and reports its driver diagnostics.</summary>
    internal static int Compile(ShaderType type, string source)
    {
        int shader = GL.CreateShader(type); GL.ShaderSource(shader, source); GL.CompileShader(shader);
        GL.GetShader(shader, ShaderParameter.CompileStatus, out int success);
        Assert.True(success != 0, GL.GetShaderInfoLog(shader));
        return shader;
    }

    /// <summary>Links the unchanged engine vertex/fragment pair as the ordinary rendering baseline.</summary>
    internal static int Link(int vertex, int fragment)
    {
        int program = GL.CreateProgram();
        try
        {
            GL.AttachShader(program, vertex); GL.AttachShader(program, fragment);
            GL.LinkProgram(program); GL.GetProgram(program, GetProgramParameterName.LinkStatus, out int linked);
            Assert.True(linked != 0, GL.GetProgramInfoLog(program));
            return program;
        }
        catch { GL.DeleteProgram(program); throw; }
    }
    #endregion
}