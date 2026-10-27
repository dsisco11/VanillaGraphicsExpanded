using VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Tests.GPU.Fixtures;

namespace VanillaGraphicsExpanded.Tests.GPU;

/// <summary>Executes the installed cube-particle vertex lighting before interpolation or fragment containment.</summary>
[Collection("GPU")]
[Trait("Category", "GPU")]
public sealed class SceneColorParticleLightingTests(HeadlessGLFixture fixture) : RenderTestBase(fixture)
{
    #region Public API
    /// <summary>Black and vanishing ambient remain finite through static, block, point and night-vision branches.</summary>
    [Theory]
    [InlineData(0, 0)] [InlineData(0, 1)] [InlineData(0, 2)] [InlineData(0, 3)]
    [InlineData(1e-30f, 0)] [InlineData(1e-30f, 1)] [InlineData(1e-30f, 2)] [InlineData(1e-30f, 3)]
    [InlineData(0, 4)] [InlineData(1e-30f, 4)]
    public void InstalledDarkParticleLightingIsFinite(float ambient, int source)
    {
        EnsureContextValid();
        float[] actual = Capture(ambient, source, 1);
        Assert.All(actual, value => Assert.True(float.IsFinite(value)));
        Assert.Equal(1, actual[3]);
        if (source is 0 or 4) Assert.InRange(actual.Take(3).Max(), 0, 1e-25f);
        else Assert.True(actual.Take(3).Max() > 0);
    }

    /// <summary>For ordinary inputs the scene guard preserves the installed native response used by offscreen rendering.</summary>
    [Theory]
    [InlineData(0)] [InlineData(1)] [InlineData(2)] [InlineData(3)]
    public void InstalledOrdinaryParticleLightingMatchesNativeResponse(int source)
    {
        EnsureContextValid();
        float[] native = Capture(.3f, source, 0), scene = Capture(.3f, source, 1);
        for (int channel = 0; channel < 4; channel++)
            Assert.InRange(Math.Abs(scene[channel] - native[channel]), 0, Math.Max(1e-6f, Math.Abs(native[channel]) * 1e-5f));
    }

    /// <summary>The decal consumer executes the same guarded source mixing in its complete installed vertex stage.</summary>
    [Theory]
    [InlineData(0)] [InlineData(1)] [InlineData(2)] [InlineData(3)] [InlineData(4)]
    public void InstalledDarkDecalLightingIsFinite(int source)
    {
        EnsureContextValid();
        float[] actual = Capture(0, source, 1, "decals.vsh");
        Assert.All(actual, value => Assert.True(float.IsFinite(value)));
        Assert.Equal(1, actual[3]);
        if (source is 0 or 4) Assert.Equal(new[] { 0f, 0f, 0f, 1f }, actual);
        else Assert.True(actual.Take(3).Max() > 0);
    }
    #endregion

    #region Private
    /// <summary>Runs the complete installed vertex stage and reads its color through a one-vertex transform-feedback buffer.</summary>
    private static float[] Capture(float ambient, int source, int scene, string family = "particlescube.vsh")
    {
        using var shaders = new TerrainShaderTestFixture();
        string code = PbrSurfaceInstalledShaderTests.Build(family, 0, 0, 0, 0, 0);
        if (source == 0) code = code.Replace("#define DYNLIGHTS 4", "#define DYNLIGHTS 0");
        Assert.Contains($"#define DYNLIGHTS {(source == 0 ? 0 : 4)}", code);
        int vertex = shaders.Compile(ShaderType.VertexShader, code);
        int program = GL.CreateProgram(), buffer = GL.GenBuffer(), vao = GL.GenVertexArray();
        try
        {
            GL.AttachShader(program, vertex);
            GL.TransformFeedbackVaryings(program, 1, ["color"], TransformFeedbackMode.InterleavedAttribs);
            GL.LinkProgram(program);
            GL.GetProgram(program, GetProgramParameterName.LinkStatus, out int linked);
            Assert.True(linked != 0, GL.GetProgramInfoLog(program));
            GL.UseProgram(program); GL.BindVertexArray(vao);
            GL.Uniform1(GL.GetUniformLocation(program, "vge_sceneLinear"), scene);
            GL.Uniform3(GL.GetUniformLocation(program, "rgbaAmbientIn"), ambient, ambient * .5f, ambient * .25f);
            GL.Uniform1(GL.GetUniformLocation(program, "nightVisionStrength"), source == 3 ? 1f : 0f);
            GL.VertexAttrib4(family == "decals.vsh" ? 2 : 7, source == 1 ? .4f : 0f, source == 1 ? .2f : 0f, source == 1 ? .1f : 0f, 0f);
            if (family == "particlescube.vsh") GL.VertexAttrib4(8, 1f, 1f, 1f, 1f);
            GL.VertexAttribI1(3, 0);
            if (source == 2)
            {
                GL.Uniform1(GL.GetUniformLocation(program, "pointLightQuantity"), 1);
                GL.Uniform3(GL.GetUniformLocation(program, "pointLights[0]"), 2f, 0f, 0f);
                GL.Uniform3(GL.GetUniformLocation(program, "pointLightColors[0]"), 4f, 2f, 1f);
            }
            GL.BindBuffer(BufferTarget.TransformFeedbackBuffer, buffer);
            GL.BufferData(BufferTarget.TransformFeedbackBuffer, 16, IntPtr.Zero, BufferUsageHint.StreamRead);
            GL.BindBufferBase(BufferRangeTarget.TransformFeedbackBuffer, 0, buffer);
            GL.Enable(EnableCap.RasterizerDiscard);
            GL.BeginTransformFeedback(TransformFeedbackPrimitiveType.Points);
            GL.DrawArrays(PrimitiveType.Points, 0, 1);
            GL.EndTransformFeedback();
            float[] actual = new float[4];
            GL.GetBufferSubData(BufferTarget.TransformFeedbackBuffer, IntPtr.Zero, 16, actual);
            Assert.Equal(ErrorCode.NoError, GL.GetError());
            return actual;
        }
        finally
        {
            GL.Disable(EnableCap.RasterizerDiscard);
            GL.BindBufferBase(BufferRangeTarget.TransformFeedbackBuffer, 0, 0);
            GL.BindVertexArray(0); GL.UseProgram(0);
            GL.DeleteBuffer(buffer); GL.DeleteVertexArray(vao); GL.DeleteProgram(program);
            OpenGlStateCache.Current.InvalidateAll();
        }
    }
    #endregion
}
