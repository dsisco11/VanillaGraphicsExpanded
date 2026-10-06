using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.LumOn;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Tests.GPU.Fixtures;

namespace VanillaGraphicsExpanded.Tests.GPU;

/// <summary>Exercises the production temporal debug reprojection helper with a perspective history matrix.</summary>
[Collection("GPU")]
[Trait("Category", "GPU")]
public sealed class LumOnTemporalDebugShaderTests : RenderTestBase
{
    /// <summary>Uses the shared graphics context for the imported GLSL helper.</summary>
    public LumOnTemporalDebugShaderTests(HeadlessGLFixture fixture) : base(fixture) { }

    #region Debug reprojection
    /// <summary>The debug helper maps current-relative points into the previous frame with the rebased homogeneous W.</summary>
    [Fact]
    public void ImportedDebugHelperUsesRebasedPerspectiveHistory()
    {
        EnsureContextValid();
        int shader = VanillaGraphicsExpanded.Tests.GPU.Helpers.BuiltShaderFixture.LoadFixture("tests/temporal-debug.csh", ShaderType.ComputeShader), program = GL.CreateProgram(), output = GL.GenBuffer();
        try
        {
            GL.AttachShader(program, shader); GL.LinkProgram(program);
            GL.GetProgram(program, GetProgramParameterName.LinkStatus, out int linked);
            Assert.True(linked != 0, GL.GetProgramInfoLog(program));
            float[] matrix = [2,0,0,0, 0,3,0,0, 0,0,-1.02f,-1, 0,0,-.202f,0];
            var history = new LumOnTemporalReprojection();
            history.Capture(matrix, 16777216.25, 32, -16777216.25); history.Commit();
            history.Capture(matrix, 16777216.375, 32.0625, -16777216.125);
            GL.UseProgram(program);
            using var inputs = new PackedUniformBuffer(96);
            byte[] inputBytes = new byte[96];
            System.Runtime.InteropServices.MemoryMarshal.AsBytes(history.PreviousViewProjection.AsSpan()).CopyTo(inputBytes);
            inputs.SetBytes(inputBytes);
            Assert.True(inputs.TryBindToSlot(GpuBindingRegistry.Ubo.ShaderInputs));
            GL.BindBuffer(BufferTarget.ShaderStorageBuffer, output);
            GL.BufferData(BufferTarget.ShaderStorageBuffer, 8, IntPtr.Zero, BufferUsageHint.DynamicRead);
            GL.BindBufferBase(BufferRangeTarget.ShaderStorageBuffer, 0, output);
            GL.DispatchCompute(1, 1, 1);
            GL.MemoryBarrier(MemoryBarrierFlags.ShaderStorageBarrierBit | MemoryBarrierFlags.BufferUpdateBarrierBit);
            float[] result = new float[2];
            GL.GetBufferSubData(BufferTarget.ShaderStorageBuffer, IntPtr.Zero, 8, result);
            // Previous-relative point=(.375,-.0625,-1.875); perspective W=1.875.
            Assert.InRange(result[0], .69999f, .70001f);
            Assert.InRange(result[1], .44999f, .45001f);
        }
        finally
        {
            GL.UseProgram(0); GL.DeleteBuffer(output); GL.DeleteProgram(program); GL.DeleteShader(shader);
            StateCache.Current.InvalidateAll();
        }
    }
    #endregion
}
