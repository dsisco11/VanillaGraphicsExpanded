using System.Runtime.CompilerServices;
using HarmonyLib;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.HarmonyPatches;
using VanillaGraphicsExpanded.ModSystems;
using VanillaGraphicsExpanded.PBR.Tessellation;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Tests.GPU.Fixtures;
using VanillaGraphicsExpanded.Tests.GPU.Helpers;
using Vintagestory.Client.NoObf;

namespace VanillaGraphicsExpanded.Tests.GPU;

/// <summary>Exercises the engine's actual grouped draw and safe candidate rejection without a game instance.</summary>
[Collection("GPU")]
[Trait("Category", "GPU")]
public sealed class TerrainTessellationEngineDrawTests : RenderTestBase
{
    /// <summary>Uses the shared headless context.</summary>
    public TerrainTessellationEngineDrawTests(HeadlessGLFixture fixture) : base(fixture) { }

    #region Production grouped draw
    /// <summary>The patched engine multidraw preserves index offsets/counts and restores patch state.</summary>
    [Fact]
    public void PatchedEngineGroupedDrawMatchesOrdinaryIndexedDraw()
    {
        EnsureContextValid();
        using var shaders = new TerrainShaderTestFixture();
        int vertex = shaders.Compile(ShaderType.VertexShader, VertexSource);
        int fragment = shaders.Compile(ShaderType.FragmentShader, FragmentSource);
        var owner = CreateOwner(shaders, vertex, fragment);
        using var vertexArray = GpuVao.Create();
        int vao = vertexArray.VertexArrayId, indices = GL.GenBuffer();
        var harmony = new Harmony("VGE.Tests.TerrainGroupedDraw");
        var previous = ShaderProgramBase.CurrentShaderProgram;
        int previousLevel = ConfigModSystem.Config.MaterialAtlas.UndisplacedTessellationLevel;
        bool previousHook = TerrainTessellationPrograms.DrawHookAvailable;
        int previousPatch = GlStateCache.Current.PatchVertices;
        try
        {
            harmony.CreateClassProcessor(typeof(TerrainTessellationDrawHook)).Patch();
            Assert.True(TerrainTessellationPrograms.DrawHookAvailable);
            GlStateCache.Current.BindVertexArray(vao); GL.BindBuffer(BufferTarget.ElementArrayBuffer, indices);
            // Three skipped sentinel indices make a lost byte offset visibly wrong.
            GL.BufferData(BufferTarget.ElementArrayBuffer, 6 * sizeof(uint), new uint[] { 2, 2, 2, 0, 1, 2 }, BufferUsageHint.StaticDraw);
            using var framework = new ShaderTestFramework();
            using var target = framework.CreateTestGBuffer(8, 8, PixelInternalFormat.Rgba32f);
            target.BindWithViewport(); GL.Disable(EnableCap.DepthTest); GL.Disable(EnableCap.Blend); GL.Disable(EnableCap.CullFace);
            GlStateCache.Current.UseProgram(owner.ProgramId);
            GL.DrawElements(PrimitiveType.Triangles, 3, DrawElementsType.UnsignedInt, 3 * sizeof(uint));
            float[] expected = target[0].ReadPixels();
            ConfigModSystem.Config.MaterialAtlas.UndisplacedTessellationLevel = 4;
            TerrainTessellationTestAssets.Prepare(owner);
            TerrainTessellationPatches.Configure(owner);
            TerrainTessellationPrograms.Prepare(owner);
            ShaderProgramBase.CurrentShaderProgram = owner;
            Assert.True(TerrainTessellationPrograms.Active);
            GlStateCache.Current.UseProgram(owner.ProgramId); GL.ClearColor(0, 0, 0, 0); GL.Clear(ClearBufferMask.ColorBufferBit);
            GlStateCache.Current.SetPatchVertices(5);
            // Installed IL uses no instance fields: this avoids creating windows, audio or a game.
            var platform = (ClientPlatformWindows)RuntimeHelpers.GetUninitializedObject(typeof(ClientPlatformWindows));
            // Uninitialized engine owners must not finalize absent game state; this test releases GL resources explicitly.
            GC.SuppressFinalize(platform);
            var mesh = (VAO)RuntimeHelpers.GetUninitializedObject(typeof(VAO));
            GC.SuppressFinalize(mesh);
            mesh.VaoId = vao; mesh.vboIdIndex = indices; mesh.drawMode = PrimitiveType.Triangles;
            int[] offsets = [3 * sizeof(uint), 0];
            int[] counts = [3];
            platform.RenderMesh(mesh, offsets, counts, 1, false);
            float[] actual = target[0].ReadPixels();
            for (int i = 0; i < actual.Length; i++) Assert.InRange(actual[i], expected[i] - .0001f, expected[i] + .0001f);
            Assert.Equal(new[] { 12, 0 }, offsets); Assert.Equal(new[] { 3 }, counts);
            Assert.Equal(PrimitiveType.Triangles, mesh.drawMode);
            Assert.Equal(5, GL.GetInteger(GetPName.PatchVertices));
            Assert.True(actual[3] > .9f);
            // A managed argument failure must also execute the installed finalizer.
            Assert.ThrowsAny<Exception>(() => platform.RenderMesh(null!, offsets, counts, 1, false));
            Assert.Equal(5, GL.GetInteger(GetPName.PatchVertices));
        }
        finally
        {
            harmony.UnpatchAll(harmony.Id);
            TerrainTessellationPrograms.Forget(owner); ShaderProgramBase.CurrentShaderProgram = previous;
            ConfigModSystem.Config.MaterialAtlas.UndisplacedTessellationLevel = previousLevel;
            TerrainTessellationPrograms.DrawHookAvailable = previousHook;
            GlStateCache.Current.SetPatchVertices(previousPatch);
            GlStateCache.Current.UseProgram(0); GlStateCache.Current.BindVertexArray(0); GL.DeleteBuffer(indices);
            GpuProgramObject.Adopt(owner.ProgramId).Dispose();
        }
    }
    #endregion

    #region Failure ownership
    /// <summary>An unsupported output array leaves the original executable and shader ownership intact.</summary>
    [Fact]
    public void UnsupportedInterfaceRetainsOrdinaryExecutable()
    {
        EnsureContextValid();
        using var shaders = new TerrainShaderTestFixture();
        int vertex = shaders.Compile(ShaderType.VertexShader, VertexSource.Replace("out vec4 rgba;", "out vec4 rgba[1];").Replace("rgba =", "rgba[0] ="));
        int fragment = shaders.Compile(ShaderType.FragmentShader, FragmentSource.Replace("in vec4 rgba;", "in vec4 rgba[1];").Replace("color=rgba;", "color=rgba[0];"));
        var owner = CreateOwner(shaders, vertex, fragment);
        int original = owner.ProgramId;
        int previousLevel = ConfigModSystem.Config.MaterialAtlas.UndisplacedTessellationLevel;
        bool previousHook = TerrainTessellationPrograms.DrawHookAvailable;
        var previousLog = TerrainTessellationPrograms.Log;
        try
        {
            ConfigModSystem.Config.MaterialAtlas.UndisplacedTessellationLevel = 4;
            TerrainTessellationPrograms.DrawHookAvailable = true;
            string diagnostic = ""; TerrainTessellationPrograms.Log = message => diagnostic += message;
            TerrainTessellationTestAssets.Prepare(owner);
            TerrainTessellationPatches.Configure(owner);
            TerrainTessellationPrograms.Prepare(owner);
            Assert.Equal(original, owner.ProgramId); Assert.True(GL.IsProgram(original));
            Assert.True(GL.IsShader(vertex)); Assert.True(GL.IsShader(fragment));
            Assert.Contains("Unsupported terrain output declaration", diagnostic);
        }
        finally
        {
            TerrainTessellationPrograms.Forget(owner); ConfigModSystem.Config.MaterialAtlas.UndisplacedTessellationLevel = previousLevel;
            TerrainTessellationPrograms.DrawHookAvailable = previousHook; TerrainTessellationPrograms.Log = previousLog;
            GpuProgramObject.Adopt(owner.ProgramId).Dispose();
        }
    }
    #endregion

    #region Fixture inputs
    /// <summary>Wraps linked stages in the same engine ownership model used by terrain.</summary>
    private static ShaderProgram CreateOwner(TerrainShaderTestFixture shaders, int vertex, int fragment) => new()
    {
        PassName = "chunkopaque", AssetDomain = "game", ProgramId = TerrainShaderTestFixture.Link(vertex, fragment),
        VertexShader = new Shader { ShaderId = vertex, Code = shaders.Source(vertex) }, FragmentShader = new Shader { ShaderId = fragment }
    };

    private const string VertexSource = """
        #version 430 core
        out vec4 rgba;
        void main() {
            vec2 p[3] = vec2[3](vec2(-1,-1),vec2(3,-1),vec2(-1,3));
            gl_Position = vec4(p[gl_VertexID],0,1);
            rgba = vec4(p[gl_VertexID] * 0.25 + 0.25,0.5,1);
        }
        """;
    private const string FragmentSource = """
        #version 430 core
        in vec4 rgba;
        layout(location=0) out vec4 color;
        void main() { color=rgba; }
        """;
    #endregion
}
