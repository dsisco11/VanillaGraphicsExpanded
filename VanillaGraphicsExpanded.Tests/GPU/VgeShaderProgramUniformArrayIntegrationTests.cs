using System;
using System.Reflection;

using OpenTK.Graphics.OpenGL;

using VanillaGraphicsExpanded.Rendering.Shaders;
using VanillaGraphicsExpanded.Tests.GPU.Fixtures;

using Vintagestory.API.MathTools;

using Xunit;

namespace VanillaGraphicsExpanded.Tests.GPU;

[Collection("GPU")]
[Trait("Category", "GPU")]
public sealed class VgeShaderProgramUniformArrayIntegrationTests
{
    private readonly HeadlessGLFixture fixture;

    public VgeShaderProgramUniformArrayIntegrationTests(HeadlessGLFixture fixture)
    {
        this.fixture = fixture;
    }

    private sealed class TestVgeProgram : VgeShaderProgram
    {
        public void SetProgramIdForTest(int programId)
        {
            if (programId <= 0) throw new ArgumentOutOfRangeException(nameof(programId));

            // ShaderProgram (engine) owns ProgramId; it's not part of this repo.
            // Set it via reflection to enable testing of the uniform helper.
            Type? t = GetType();
            while (t is not null)
            {
                var prop = t.GetProperty(
                    "ProgramId",
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

                if (prop is not null && prop.CanWrite)
                {
                    prop.SetValue(this, programId);
                    return;
                }

                // Common backing-field pattern for auto-properties.
                var backing = t.GetField(
                    "<ProgramId>k__BackingField",
                    BindingFlags.Instance | BindingFlags.NonPublic);
                if (backing is not null)
                {
                    backing.SetValue(this, programId);
                    return;
                }

                var field = t.GetField(
                    "ProgramId",
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (field is not null)
                {
                    field.SetValue(this, programId);
                    return;
                }

                t = t.BaseType;
            }

            throw new InvalidOperationException("Unable to set ProgramId on ShaderProgram for testing.");
        }

        public bool TrySetArrayElement(string uniformName, int index, Vec3f value)
        {
            return TryUniformArrayElement(uniformName, index, value);
        }
    }

    [Fact]
    public void TryUniformArrayElement_SetsCorrectElementValues()
    {
        fixture.MakeCurrent();

        int programId = CompileMinimalProgram(VertexShaderSource, FragmentShaderSource);
        Assert.True(programId > 0);

        int prevProgram = GL.GetInteger(GetPName.CurrentProgram);
        try
        {
            GL.UseProgram(programId);

            var prog = new TestVgeProgram();
            prog.SetProgramIdForTest(programId);

            // Seed known values.
            Vec3f[] initial =
            [
                new Vec3f(1f, 2f, 3f),
                new Vec3f(4f, 5f, 6f),
                new Vec3f(7f, 8f, 9f),
                new Vec3f(-1f, -2f, -3f)
            ];

            for (int i = 0; i < initial.Length; i++)
            {
                Assert.True(prog.TrySetArrayElement("uArr", i, initial[i]));
            }

            // Overwrite a single element; others must remain unchanged.
            Vec3f updated = new(9.25f, -10.5f, 0.125f);
            Assert.True(prog.TrySetArrayElement("uArr", 2, updated));

            AssertUniformVec3ArrayElementEquals(programId, "uArr", 0, initial[0]);
            AssertUniformVec3ArrayElementEquals(programId, "uArr", 1, initial[1]);
            AssertUniformVec3ArrayElementEquals(programId, "uArr", 2, updated);
            AssertUniformVec3ArrayElementEquals(programId, "uArr", 3, initial[3]);
        }
        finally
        {
            GL.UseProgram(prevProgram);
            GL.DeleteProgram(programId);
        }
    }

    private static void AssertUniformVec3ArrayElementEquals(int programId, string uniformName, int index, Vec3f expected)
    {
        int loc0 = GL.GetUniformLocation(programId, uniformName + "[0]");
        Assert.True(loc0 >= 0, $"Uniform '{uniformName}[0]' not found/active");

        float[] values = new float[3];
        GL.GetUniform(programId, loc0 + index, values);

        AssertFloatEqual(expected.X, values[0]);
        AssertFloatEqual(expected.Y, values[1]);
        AssertFloatEqual(expected.Z, values[2]);
    }

    private static void AssertFloatEqual(float expected, float actual)
    {
        const float eps = 1e-6f;
        Assert.True(MathF.Abs(expected - actual) <= eps, $"Expected {expected} but got {actual}");
    }

    private static int CompileMinimalProgram(string vertexSource, string fragmentSource)
    {
        int v = CompileShader(ShaderType.VertexShader, vertexSource);
        int f = CompileShader(ShaderType.FragmentShader, fragmentSource);

        int program = GL.CreateProgram();
        GL.AttachShader(program, v);
        GL.AttachShader(program, f);
        GL.LinkProgram(program);

        GL.GetProgram(program, GetProgramParameterName.LinkStatus, out int ok);
        if (ok == 0)
        {
            string log = GL.GetProgramInfoLog(program);
            GL.DeleteProgram(program);
            GL.DeleteShader(v);
            GL.DeleteShader(f);
            throw new InvalidOperationException("Link failed:\n" + log);
        }

        GL.DeleteShader(v);
        GL.DeleteShader(f);
        return program;
    }

    private static int CompileShader(ShaderType type, string source)
    {
        int id = GL.CreateShader(type);
        GL.ShaderSource(id, source);
        GL.CompileShader(id);
        GL.GetShader(id, ShaderParameter.CompileStatus, out int ok);
        if (ok == 0)
        {
            string log = GL.GetShaderInfoLog(id);
            GL.DeleteShader(id);
            throw new InvalidOperationException($"{type} compile failed:\n" + log);
        }

        return id;
    }

    private const string VertexShaderSource = """
        #version 330 core
        void main() {
            // Single full-screen triangle via gl_VertexID (no VAO needed in core if we bind one).
            vec2 pos;
            if (gl_VertexID == 0) pos = vec2(-1.0, -1.0);
            else if (gl_VertexID == 1) pos = vec2( 3.0, -1.0);
            else pos = vec2(-1.0,  3.0);
            gl_Position = vec4(pos, 0.0, 1.0);
        }
        """;

    private const string FragmentShaderSource = """
        #version 330 core
        out vec4 outColor;
        uniform vec3 uArr[4];
        void main() {
            // Ensure all array elements are active (not optimized out).
            vec3 s = uArr[0] + uArr[1] + uArr[2] + uArr[3];
            outColor = vec4(s, 1.0);
        }
        """;
}
