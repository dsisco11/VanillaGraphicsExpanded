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
        string declarations = """
            #version 430
            layout(local_size_x=1) in;
            layout(std430,binding=0) buffer Result { vec2 result; };
            uniform mat4 prevViewProjMatrix;
            uniform sampler2D probeAnchorPosition, probeAnchorNormal, historyMeta;
            uniform int probeSpacing, debugMode;
            uniform vec2 probeGridSize;
            uniform float depthRejectThreshold, normalRejectThreshold, temporalAlpha;
            vec3 worldToViewPos(vec3 p) { return p; }
            vec3 lumonDecodeNormal(vec3 n) { return n; }
            mat4 getViewMatrix() { return mat4(1); }
            """;
        string source = declarations + "\n" + File.ReadAllText(Path.Combine(AppContext.BaseDirectory,
            "assets", "shaders", "includes", "debug", "reproject_to_history.glsl")) +
            "\nvoid main(){result=reprojectToHistory(vec3(.25,-.125,-2));}";
        int shader = GL.CreateShader(ShaderType.ComputeShader), program = GL.CreateProgram(), output = GL.GenBuffer();
        try
        {
            GL.ShaderSource(shader, source); GL.CompileShader(shader);
            GL.GetShader(shader, ShaderParameter.CompileStatus, out int compiled);
            Assert.True(compiled != 0, GL.GetShaderInfoLog(shader));
            GL.AttachShader(program, shader); GL.LinkProgram(program);
            GL.GetProgram(program, GetProgramParameterName.LinkStatus, out int linked);
            Assert.True(linked != 0, GL.GetProgramInfoLog(program));
            float[] matrix = [2,0,0,0, 0,3,0,0, 0,0,-1.02f,-1, 0,0,-.202f,0];
            var history = new LumOnTemporalReprojection();
            history.Capture(matrix, 16777216.25, 32, -16777216.25); history.Commit();
            history.Capture(matrix, 16777216.375, 32.0625, -16777216.125);
            GL.UseProgram(program);
            GL.UniformMatrix4(GL.GetUniformLocation(program, "prevViewProjMatrix"), 1, false, history.PreviousViewProjection);
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
            GlStateCache.Current.InvalidateAll();
        }
    }
    #endregion
}
