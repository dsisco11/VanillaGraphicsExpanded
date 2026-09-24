using System.Text.RegularExpressions;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.HarmonyPatches;
using VanillaGraphicsExpanded.LumOn.Scene;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Tests.GPU.Fixtures;
using Vintagestory.Client.NoObf;

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
    [Fact]
    public void TerrainHook_PublishesChunkWindowAndRebindsAfterAnotherPass()
    {
        EnsureContextValid();
        TerrainLumonSceneChunkSlotUniformBindingHook.ClearUniformCache();
        string includes = Path.Combine(AppContext.BaseDirectory, "assets", "shaders", "includes");
        string source = "#version 430\n" + Expand(Path.Combine(includes, "lumonscene_chunkslot.glsl")) +
            Expand(Path.Combine(includes, "lumonscene_patchid.glsl")) + """

            layout(local_size_x=1) in;
            layout(std430,binding=0) buffer Result { uvec4 value; };
            void main() {
                vec3 position=vec3(0.0);
                uint slot; bool inside=VgeLumonSceneTryMapChunkCoordToSlot(VgeLumonSceneChunkCoordFromWorldPos(position),slot);
                uint patchId; vec2 uv; VgeLumonSceneComputeVoxelPatchIdAndUv(position,vec3(0,1,0),patchId,uv);
                value=uvec4(slot,patchId,VgeLumonSceneGetChunkSlotGeneration16(slot),inside?1u:0u);
            }
            """;
        int shader = GL.CreateShader(ShaderType.ComputeShader), program = GL.CreateProgram();
        int output = GL.GenBuffer(), unrelated = GL.GenBuffer();
        using var generations = Texture2D.Create(289, 1, PixelInternalFormat.R32ui, TextureFilterMode.Nearest);
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
            GL.BufferData(BufferTarget.ShaderStorageBuffer, 16, IntPtr.Zero, BufferUsageHint.DynamicRead);
            GL.BindBufferBase(BufferRangeTarget.ShaderStorageBuffer, 0, output);
            GL.BindBuffer(BufferTarget.UniformBuffer, unrelated);
            GL.BufferData(BufferTarget.UniformBuffer, 48, new int[12], BufferUsageHint.DynamicDraw);
            var engineProgram = new ShaderProgramChunkopaque { ProgramId = program };
            for (int pass = 0; pass < 2; pass++)
            {
                // A zeroed foreign object block reproduces the missing window binding deterministically.
                GL.BindBufferBase(BufferRangeTarget.UniformBuffer, GpuBindingRegistry.Ubo.Object, unrelated);
                GL.UseProgram(program);
                TerrainLumonSceneChunkSlotUniformBindingHook.Use_Postfix(engineProgram);
                GL.DispatchCompute(1, 1, 1);
                GL.MemoryBarrier(MemoryBarrierFlags.ShaderStorageBarrierBit | MemoryBarrierFlags.BufferUpdateBarrierBit);
                uint[] actual = new uint[4];
                GL.BindBuffer(BufferTarget.ShaderStorageBuffer, output);
                GL.GetBufferSubData(BufferTarget.ShaderStorageBuffer, IntPtr.Zero, 16, actual);
                // Local chunk(8,0,8) plus ring(3,0,5) => slot13*17+11=232.
                Assert.Equal(new uint[] { 232, 1053, 1232, 1 }, actual);
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
