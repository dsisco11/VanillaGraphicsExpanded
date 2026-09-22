using OpenTK.Graphics.OpenGL;

using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Tests.GPU.Fixtures;
using VanillaGraphicsExpanded.Tests.GPU.Helpers;

namespace VanillaGraphicsExpanded.Tests.GPU;

[Collection("GPU")]
[Trait("Category", "GPU")]
public sealed class PbrMaterialParamsTextureSmokeTests
{
    private readonly HeadlessGLFixture fixture;

    public PbrMaterialParamsTextureSmokeTests(HeadlessGLFixture fixture)
    {
        this.fixture = fixture;
    }

    [Fact]
    public void Rgb16fTexture_CanSampleWithoutGlErrors()
    {
        fixture.EnsureContextValid();
        fixture.MakeCurrent();

        using var framework = new ShaderTestFramework();

        const int width = 4;
        const int height = 4;

        // RGB16F texture: (roughness, metallic, emissive)
        var data = new float[width * height * 3];
        for (int i = 0; i < data.Length; i += 3)
        {
            data[i + 0] = 0.25f;
            data[i + 1] = 0.50f;
            data[i + 2] = 0.75f;
        }

        using DynamicTexture2D materialTex = framework.CreateTexture(width, height, PixelInternalFormat.Rgb16f, data, TextureFilterMode.Nearest);

        Assert.True(materialTex.IsValid);
        Assert.Equal(width, materialTex.Width);
        Assert.Equal(height, materialTex.Height);
        Assert.Equal(PixelInternalFormat.Rgb16f, materialTex.InternalFormat);

        // Validate upload via readback (driver-independent)
        var uploaded = materialTex.ReadPixels();
        Assert.Equal(data.Length, uploaded.Length);
        Assert.Equal(0.25f, uploaded[0], 2);
        Assert.Equal(0.50f, uploaded[1], 2);
        Assert.Equal(0.75f, uploaded[2], 2);

        // Bind input texture (and clear any prior GL error state)
        materialTex.Bind(0);
        _ = GL.GetError();

        using var output = framework.CreateTestGBuffer(1, 1, PixelInternalFormat.Rgba16f, 1);

        int programId = CompileMinimalProgram(VertexShaderSource, FragmentShaderSource);
        try
        {
            GL.UseProgram(programId);

            int loc = global::VanillaGraphicsExpanded.Tests.GPU.Helpers.TestShaderInterfaces.GetUniformLocation(programId, "materialParams");
            Assert.True(loc >= 0);
            GL.Uniform1(loc, 0);

            output.BindWithViewport();
            GL.ClearColor(0, 0, 0, 0);
            GL.Clear(ClearBufferMask.ColorBufferBit);

            framework.RenderQuad(programId);

            Assert.Equal(ErrorCode.NoError, GL.GetError());

            // Smoke: the important part is that we can bind/sampler/render without GL errors.
            _ = output[0].ReadPixels();
        }
        finally
        {
            GL.UseProgram(0);
            if (programId != 0)
            {
                global::VanillaGraphicsExpanded.Tests.GPU.Helpers.TestShaderInterfaces.DeleteProgram(programId);
            }
        }
    }

    private static int CompileMinimalProgram(string vertexSource, string fragmentSource)
    {
        int v = VanillaGraphicsExpanded.Tests.GPU.Helpers.BuiltShaderFixture.Load(vertexSource, ShaderType.VertexShader);
        GL.GetShader(v, ShaderParameter.CompileStatus, out int vStatus);
        if (vStatus != (int)All.True)
        {
            string log = GL.GetShaderInfoLog(v);
            global::VanillaGraphicsExpanded.Tests.GPU.Helpers.TestShaderInterfaces.DeleteShader(v);
            throw new InvalidOperationException($"Vertex shader compile error: {log}");
        }

        int f = VanillaGraphicsExpanded.Tests.GPU.Helpers.BuiltShaderFixture.Load(fragmentSource, ShaderType.FragmentShader);
        GL.GetShader(f, ShaderParameter.CompileStatus, out int fStatus);
        if (fStatus != (int)All.True)
        {
            string log = GL.GetShaderInfoLog(f);
            global::VanillaGraphicsExpanded.Tests.GPU.Helpers.TestShaderInterfaces.DeleteShader(v);
            global::VanillaGraphicsExpanded.Tests.GPU.Helpers.TestShaderInterfaces.DeleteShader(f);
            throw new InvalidOperationException($"Fragment shader compile error: {log}");
        }

        int program = GL.CreateProgram();
        GL.AttachShader(program, v);
        GL.AttachShader(program, f);
        global::VanillaGraphicsExpanded.Tests.GPU.Helpers.TestShaderInterfaces.LinkProgram(program);

        GL.GetProgram(program, GetProgramParameterName.LinkStatus, out int linkStatus);
        if (linkStatus != (int)All.True)
        {
            string log = GL.GetProgramInfoLog(program);
            global::VanillaGraphicsExpanded.Tests.GPU.Helpers.TestShaderInterfaces.DeleteProgram(program);
            global::VanillaGraphicsExpanded.Tests.GPU.Helpers.TestShaderInterfaces.DeleteShader(v);
            global::VanillaGraphicsExpanded.Tests.GPU.Helpers.TestShaderInterfaces.DeleteShader(f);
            throw new InvalidOperationException($"Program link error: {log}");
        }

        GL.DetachShader(program, v);
        GL.DetachShader(program, f);
        global::VanillaGraphicsExpanded.Tests.GPU.Helpers.TestShaderInterfaces.DeleteShader(v);
        global::VanillaGraphicsExpanded.Tests.GPU.Helpers.TestShaderInterfaces.DeleteShader(f);

        return program;
    }

    private const string VertexShaderSource = "tests/PbrMaterialParamsTextureSmokeTests_1.vsh";

    private const string FragmentShaderSource = "tests/PbrMaterialParamsTextureSmokeTests_2.fsh";
}
