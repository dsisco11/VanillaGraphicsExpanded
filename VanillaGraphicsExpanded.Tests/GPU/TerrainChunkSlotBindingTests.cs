using System.Text.RegularExpressions;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.HarmonyPatches;
using VanillaGraphicsExpanded.LumOn;
using VanillaGraphicsExpanded.LumOn.Scene;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Tests.GPU.Fixtures;
using Vintagestory.Client.NoObf;
using Vintagestory.API.Client;

namespace VanillaGraphicsExpanded.Tests.GPU;

/// <summary>Exercises the vanilla terrain binding hook with the actual imported chunk and patch mapping functions.</summary>
[Collection("GPU")]
[Trait("Category", "GPU")]
public sealed class TerrainChunkSlotBindingTests : RenderTestBase
{
    #region Construction
    /// <summary>Uses the shared graphics context for engine-style GLSL and uniform binding.</summary>
    public TerrainChunkSlotBindingTests(HeadlessGLFixture fixture) : base(fixture) { }
    #endregion

    #region Terrain mapping
    /// <summary>Large-world terrain maps to a nonzero toroidal slot after unrelated passes overwrite the object binding.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void TerrainHook_PublishesChunkWindowAndRebindsAfterAnotherPass(bool movingPlayer)
    {
        EnsureContextValid();
        TerrainLumonSceneChunkSlotUniformBindingHook.ClearUniformCache();
        string includes = Path.Combine(AppContext.BaseDirectory, "assets", "shaders", "includes");
        string source = "#version 430\n" + Expand(Path.Combine(includes, "lumonscene_chunkslot.glsl")) +
            Expand(Path.Combine(includes, "lumonscene_patchid.glsl")) + """

            layout(local_size_x=1) in;
            layout(std430,binding=0) buffer Result { uvec4 value; vec4 patchUv; };
            uniform vec3 position;
            void main() {
                uint slot; bool inside=VgeLumonSceneTryMapChunkCoordToSlot(VgeLumonSceneChunkCoordFromWorldPos(position),slot);
                uint patchId; vec2 uv; VgeLumonSceneComputeVoxelPatchIdAndUv(position,vec3(0,1,0),patchId,uv);
                value=uvec4(slot,patchId,VgeLumonSceneGetChunkSlotGeneration16(slot),inside?1u:0u);
                patchUv=vec4(uv,0,0);
            }
            """;
        int shader = GL.CreateShader(ShaderType.ComputeShader), program = GL.CreateProgram();
        int output = GL.GenBuffer(), unrelated = GL.GenBuffer();
        using var generations = Texture2D.Create(289, 1, PixelInternalFormat.R32ui, TextureFilterMode.Nearest);
        using var assets = new BinaryShaderApiFixture();
        var events = new RuntimeRenderEvents();
        var api = RuntimeRenderEvents.Adapt<ICoreClientAPI>((method, args) => method.Name == "get_Event"
            ? events.Api : method.Invoke(assets.Api, args));
        var config = new VgeConfig();
        config.LumOn.Enabled = config.LumOn.LumonScene.Enabled = true;
        const double surfaceX = 512030.11, surfaceY = 3, surfaceZ = 511990.5;
        LumOnCameraState camera = new(surfaceX, surfaceY, surfaceZ, surfaceX, surfaceY, surfaceZ, 0);
        using var publisher = new LumOnTerrainBridgeUpdateRenderer(api, config, () => camera);
        try
        {
            GL.ShaderSource(shader, source); GL.CompileShader(shader);
            GL.GetShader(shader, ShaderParameter.CompileStatus, out int compiled);
            Assert.True(compiled != 0, GL.GetShaderInfoLog(shader));
            GL.AttachShader(program, shader); GL.LinkProgram(program);
            GL.GetProgram(program, GetProgramParameterName.LinkStatus, out int linked);
            Assert.True(linked != 0, GL.GetProgramInfoLog(program));
            generations.UploadDataImmediate(Enumerable.Range(0, 289).Select(i => (uint)(1000 + i)).ToArray());
            LumOnTerrainBridgeUboState.Update(new(16000, 0, 15999), new(30.11, 3, 22.5));
            LumonSceneChunkSlotUniformState.Update(new(15992, 0, 15991), new(17, 1, 17), new(3, 0, 5), generations.TextureId);
            GL.BindBuffer(BufferTarget.ShaderStorageBuffer, output);
            GL.BufferData(BufferTarget.ShaderStorageBuffer, 32, IntPtr.Zero, BufferUsageHint.DynamicRead);
            GL.BindBufferBase(BufferRangeTarget.ShaderStorageBuffer, 0, output);
            GL.BindBuffer(BufferTarget.UniformBuffer, unrelated);
            GL.BufferData(BufferTarget.UniformBuffer, 48, new int[12], BufferUsageHint.DynamicDraw);
            var engineProgram = new ShaderProgramChunkopaque { ProgramId = program };
            foreach (double displacement in movingPlayer ? new[] { 0, .25, -.4, 2.25, 31.99, 32.25 } : new[] { 0.0, 0.0 })
            {
                camera = camera with { PositionX = surfaceX + displacement, PositionZ = surfaceZ - displacement,
                    CameraX = surfaceX + displacement + .1, CameraY = surfaceY + 1.6 + displacement * .01, CameraZ = surfaceZ - displacement - .2 };
                if (movingPlayer) events.Render(EnumRenderStage.Opaque);
                // A zeroed foreign object block reproduces the missing window binding deterministically.
                GL.BindBufferBase(BufferRangeTarget.UniformBuffer, GpuBindingRegistry.Ubo.Object, unrelated);
                GL.UseProgram(program);
                TerrainLumonSceneChunkSlotUniformBindingHook.Use_Postfix(engineProgram);
                // The engine subtracts CameraPos from poolOrigin before adding terrain vertices.
                GL.Uniform3(GL.GetUniformLocation(program, "position"),
                    movingPlayer ? (float)(surfaceX - camera.CameraX) : 0f,
                    movingPlayer ? (float)(surfaceY - camera.CameraY) : 0f,
                    movingPlayer ? (float)(surfaceZ - camera.CameraZ) : 0f);
                GL.DispatchCompute(1, 1, 1);
                GL.MemoryBarrier(MemoryBarrierFlags.ShaderStorageBarrierBit | MemoryBarrierFlags.BufferUpdateBarrierBit);
                uint[] actual = new uint[8];
                GL.BindBuffer(BufferTarget.ShaderStorageBuffer, output);
                GL.GetBufferSubData(BufferTarget.ShaderStorageBuffer, IntPtr.Zero, 32, actual);
                // Local chunk(8,0,8) plus ring(3,0,5) => slot13*17+11=232.
                Assert.Equal(new uint[] { 232, 1053, 1232, 1 }, actual.Take(4));
                Assert.InRange(BitConverter.UInt32BitsToSingle(actual[4]), .52749f, .52751f);
                Assert.InRange(BitConverter.UInt32BitsToSingle(actual[5]), .62499f, .62501f);
            }
        }
        finally
        {
            TerrainLumonSceneChunkSlotUniformBindingHook.ClearUniformCache();
            LumonSceneChunkSlotUniformState.Disable();
            LumOnTerrainBridgeUboState.Dispose();
            GL.UseProgram(0); GL.DeleteProgram(program); GL.DeleteShader(shader);
            GL.DeleteBuffer(output); GL.DeleteBuffer(unrelated);
            GlStateCache.Current.InvalidateAll();
        }
    }
    #endregion

    #region Source fixture
    /// <summary>Expands only relative production imports, retaining their include guards and mapping implementation unchanged.</summary>
    private static string Expand(string path)
    {
        string source = File.ReadAllText(path);
        return Regex.Replace(source, "@import\\s+\"([^\"]+)\"", match =>
            Expand(Path.GetFullPath(Path.Combine(Path.GetDirectoryName(path)!, match.Groups[1].Value))));
    }
    #endregion
}
