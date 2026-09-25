using System.Text.RegularExpressions;
using System.Reflection;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.HarmonyPatches;
using VanillaGraphicsExpanded.LumOn;
using VanillaGraphicsExpanded.LumOn.Scene;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.PBR;
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
    [InlineData(false, -1, 0)]
    [InlineData(true, -1, 0)]
    [InlineData(true, 0, 32)]
    [InlineData(true, 1, 32)]
    [InlineData(true, 2, 32)]
    [InlineData(true, 3, 32)]
    [InlineData(true, 4, 32)]
    [InlineData(true, 5, 32)]
    [InlineData(true, 0, -32)]
    [InlineData(true, 1, -32)]
    [InlineData(true, 2, -32)]
    [InlineData(true, 3, -32)]
    [InlineData(true, 4, -32)]
    [InlineData(true, 5, -32)]
    public void TerrainHook_PublishesChunkWindowAndRebindsAfterAnotherPass(bool movingPlayer, int face, int boundary)
    {
        EnsureContextValid();
        TerrainLumonSceneChunkSlotUniformBindingHook.ClearUniformCache();
        string includes = Path.Combine(AppContext.BaseDirectory, "assets", "shaders", "includes");
        // Execute the same output snippet injected into terrain rather than a parallel test implementation.
        string writes = (string)typeof(VanillaShaderPatches).GetField("GBufferOutputWrites_Chunk",
            BindingFlags.NonPublic | BindingFlags.Static)!.GetRawConstantValue()!;
        string patchWrites = writes[writes.IndexOf("uint patchId =", StringComparison.Ordinal)..];
        string source = "#version 430\n" + Expand(Path.Combine(includes, "lumonscene_chunkslot.glsl")) +
            Expand(Path.Combine(includes, "lumonscene_patchid.glsl")) + """

            layout(local_size_x=1) in;
            layout(std430,binding=0) buffer Result { uvec4 value; vec4 patchUv; };
            uniform vec3 position;
            uniform vec3 normal;
            void main() {
                vec4 worldPos=vec4(position,1); uvec4 vge_outPatchId;
            """ + patchWrites + """
                value=uvec4(vge_outPatchId.xy,vge_outPatchId.w,vge_outPatchId.y>0u?1u:0u);
                patchUv=vec4(vge_patchUv,0,0);
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
        double[] surface = face < 0 ? [512030.11, 3, 511990.5] : [2.25, 6.5, 10.75];
        float[] normal = [0, 1, 0];
        uint expectedSlot = 232, expectedPatch = 1053;
        float expectedU = .5275f, expectedV = .625f;
        if (face >= 0)
        {
            int axis = face / 2;
            surface[axis] = boundary;
            normal = new float[3]; normal[axis] = face % 2 == 0 ? 1 : -1;
            // The face's solid owner is immediately below a positive-facing boundary.
            int[] owner = surface.Select(value => (int)Math.Floor(value)).ToArray();
            if (face % 2 == 0) owner[axis]--;
            int sx = ((owner[0] >> 5) + 3) % 5;
            int sy = ((owner[1] >> 5) + 4) % 5;
            int sz = ((owner[2] >> 5) + 5) % 5;
            expectedSlot = (uint)((sy * 5 + sz) * 5 + sx);
            int uAxis = axis == 0 ? 2 : 0, vAxis = axis == 1 ? 2 : 1;
            int u = owner[uAxis] & 31, v = owner[vAxis] & 31;
            expectedPatch = (uint)(1 + face + (((owner[axis] & 31) << 6) + ((v >> 2) << 3) + (u >> 2)) * 6);
            expectedU = (float)((u % 4 + surface[uAxis] - Math.Floor(surface[uAxis])) / 4);
            expectedV = (float)((v % 4 + surface[vAxis] - Math.Floor(surface[vAxis])) / 4);
        }
        double surfaceX = surface[0], surfaceY = surface[1], surfaceZ = surface[2];
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
            if (face >= 0) LumonSceneChunkSlotUniformState.Update(new(-2, -2, -2), new(5, 5, 5), new(1, 2, 3), generations.TextureId);
            GL.BindBuffer(BufferTarget.ShaderStorageBuffer, output);
            GL.BufferData(BufferTarget.ShaderStorageBuffer, 32, IntPtr.Zero, BufferUsageHint.DynamicRead);
            GL.BindBufferBase(BufferRangeTarget.ShaderStorageBuffer, 0, output);
            GL.BindBuffer(BufferTarget.UniformBuffer, unrelated);
            GL.BufferData(BufferTarget.UniformBuffer, 48, new int[12], BufferUsageHint.DynamicDraw);
            var engineProgram = new ShaderProgramChunkopaque { ProgramId = program };
            uint[] finalMapping = [];
            foreach (double displacement in movingPlayer ? new[] { 0, .25, -.4, 2.25, 31.99, 32.25 } : new[] { 0.0, 0.0 })
            {
                camera = camera with { PositionX = surfaceX + displacement, PositionZ = surfaceZ - displacement,
                    CameraX = surfaceX + displacement + .1, CameraY = surfaceY + 1.6 + displacement * .01, CameraZ = surfaceZ - displacement - .2 };
                if (movingPlayer) events.Render(EnumRenderStage.Opaque);
                // A zeroed foreign object block reproduces the missing window binding deterministically.
                GL.BindBufferBase(BufferRangeTarget.UniformBuffer, GpuBindingRegistry.Ubo.Object, unrelated);
                GL.UseProgram(program);
                TerrainLumonSceneChunkSlotUniformBindingHook.Use_Postfix(engineProgram);
                GL.Uniform3(GL.GetUniformLocation(program, "normal"), normal[0], normal[1], normal[2]);
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
                finalMapping = actual;
                Assert.Equal(new uint[] { expectedSlot, expectedPatch, 1000 + expectedSlot, 1 }, actual.Take(4));
                Assert.InRange(BitConverter.UInt32BitsToSingle(actual[4]), expectedU - .00001f, expectedU + .00001f);
                Assert.InRange(BitConverter.UInt32BitsToSingle(actual[5]), expectedV - .00001f, expectedV + .00001f);
            }
            if (face >= 0) TerrainBoundaryCaptureFixture.AssertSelectedSurface(assets.Api, finalMapping, face, boundary);
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
