using VanillaGraphicsExpanded.PBR.Materials;
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
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void PatchedEngineGroupedDrawMatchesOrdinaryIndexedDraw(bool ordinaryDraw)
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
        var previousMode = ConfigModSystem.Config.MaterialAtlas.TerrainSurfaceDetailMode;
        bool previousHook = TerrainTessellationPrograms.DrawHookAvailable; bool previousMeshHook = TerrainTessellationPrograms.MeshDrawHookAvailable;
        int previousPatch = StateCache.Current.PatchVertices;
        try
        {
            harmony.CreateClassProcessor(typeof(TerrainTessellationDrawHook)).Patch();
            Assert.True(TerrainTessellationPrograms.DrawHookAvailable);
            StateCache.Current.BindVertexArray(vao); GL.BindBuffer(BufferTarget.ElementArrayBuffer, indices);
            // Grouped draws skip sentinel indices; ordinary draws use the first triangle directly.
            GL.BufferData(BufferTarget.ElementArrayBuffer, 6 * sizeof(uint),
                ordinaryDraw ? new uint[] { 0, 1, 2, 0, 1, 2 } : new uint[] { 2, 2, 2, 0, 1, 2 }, BufferUsageHint.StaticDraw);
            using var framework = new ShaderTestFramework();
            using var target = framework.CreateTestGBuffer(8, 8, PixelInternalFormat.Rgba32f);
            target.BindWithViewport(); GL.Disable(EnableCap.DepthTest); GL.Disable(EnableCap.Blend); GL.Disable(EnableCap.CullFace);
            StateCache.Current.UseProgram(owner.ProgramId);
            GL.DrawElements(PrimitiveType.Triangles, 3, DrawElementsType.UnsignedInt, 3 * sizeof(uint));
            float[] expected = target[0].ReadPixels();
            ConfigModSystem.Config.MaterialAtlas.TerrainSurfaceDetailMode = 2;
            TerrainTessellationTestAssets.Prepare(owner);
            TerrainTessellationPatches.Configure(owner);
            TerrainTessellationPrograms.Prepare(owner);
            owner.PopulateDisplacementUniforms();
            ShaderProgramBase.CurrentShaderProgram = owner;
            Assert.True(TerrainTessellationPrograms.Active);
            StateCache.Current.UseProgram(owner.ProgramId);
            owner.Uniform("vge_displacementTex", 13);
            owner.Uniform("vge_displacementRecords", 14);
            owner.Uniform("vge_normalDepthTex", 15);
            GL.ClearColor(0, 0, 0, 0); GL.Clear(ClearBufferMask.ColorBufferBit);
            StateCache.Current.SetPatchVertices(5);
            // Installed IL uses no instance fields: this avoids creating windows, audio or a game.
            var platform = (ClientPlatformWindows)RuntimeHelpers.GetUninitializedObject(typeof(ClientPlatformWindows));
            // Uninitialized engine owners must not finalize absent game state; this test releases GL resources explicitly.
            GC.SuppressFinalize(platform);
            var mesh = (VAO)RuntimeHelpers.GetUninitializedObject(typeof(VAO));
            GC.SuppressFinalize(mesh);
            mesh.VaoId = vao; mesh.vboIdIndex = indices; mesh.drawMode = PrimitiveType.Triangles;
            mesh.IndicesCount = 3;
            int[] offsets = [3 * sizeof(uint), 0];
            int[] counts = [3];
            if (ordinaryDraw) platform.RenderMesh(mesh);
            else platform.RenderMesh(mesh, offsets, counts, 1, false);
            Assert.Equal(ErrorCode.NoError, GL.GetError());
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
            ConfigModSystem.Config.MaterialAtlas.TerrainSurfaceDetailMode = previousMode;
            TerrainTessellationPrograms.DrawHookAvailable = previousHook; TerrainTessellationPrograms.MeshDrawHookAvailable = previousMeshHook;
            StateCache.Current.SetPatchVertices(previousPatch);
            StateCache.Current.UseProgram(0); StateCache.Current.BindVertexArray(0); GL.DeleteBuffer(indices);
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
        var previousMode = ConfigModSystem.Config.MaterialAtlas.TerrainSurfaceDetailMode;
        bool previousHook = TerrainTessellationPrograms.DrawHookAvailable; bool previousMeshHook = TerrainTessellationPrograms.MeshDrawHookAvailable;
        var previousLog = TerrainTessellationPrograms.Log;
        try
        {
            ConfigModSystem.Config.MaterialAtlas.TerrainSurfaceDetailMode = 2;
            TerrainTessellationPrograms.DrawHookAvailable = true; TerrainTessellationPrograms.MeshDrawHookAvailable = true;
            string diagnostic = ""; TerrainTessellationPrograms.Log = message => diagnostic += message;
            TerrainTessellationTestAssets.Prepare(owner);
            TerrainTessellationPatches.Configure(owner);
            TerrainTessellationPrograms.Prepare(owner);
            owner.PopulateDisplacementUniforms();
            Assert.Equal(original, owner.ProgramId); Assert.True(GL.IsProgram(original));
            Assert.True(GL.IsShader(vertex)); Assert.True(GL.IsShader(fragment));
            Assert.Contains("Unsupported terrain output declaration", diagnostic);
        }
        finally
        {
            TerrainTessellationPrograms.Forget(owner); ConfigModSystem.Config.MaterialAtlas.TerrainSurfaceDetailMode = previousMode;
            TerrainTessellationPrograms.DrawHookAvailable = previousHook; TerrainTessellationPrograms.MeshDrawHookAvailable = previousMeshHook; TerrainTessellationPrograms.Log = previousLog;
            GpuProgramObject.Adopt(owner.ProgramId).Dispose();
        }
    }
    #endregion

    #region Fixture inputs
    /// <summary>Wraps linked stages in the same engine ownership model used by terrain.</summary>
    private static TerrainLinkedTestProgram CreateOwner(TerrainShaderTestFixture shaders, int vertex, int fragment) => new()
    {
        PassName = "chunkopaque", AssetDomain = "game", ProgramId = TerrainShaderTestFixture.Link(vertex, fragment),
        VertexShader = new Shader { ShaderId = vertex, Code = shaders.Source(vertex) }, FragmentShader = new Shader { ShaderId = fragment }
    };

    private const string VertexSource = """
        #version 430 core
        out vec4 rgba;
        out vec4 worldPos;
        out vec3 normal;
        out vec2 uv;
        flat out vec2 vge_uvBase;
        flat out vec2 vge_uvExtent;
        flat out int renderFlags;
        void main() {
            vec2 p[3] = vec2[3](vec2(-1,-1),vec2(3,-1),vec2(-1,3));
            gl_Position = vec4(p[gl_VertexID],0,1);
            rgba = vec4(p[gl_VertexID] * 0.25 + 0.25,0.5,1);
            worldPos = gl_Position;
            normal = vec3(0, 0, 1);
            uv = vec2(0);
            vge_uvBase = vec2(0);
            vge_uvExtent = vec2(1);
            renderFlags = 0;
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
