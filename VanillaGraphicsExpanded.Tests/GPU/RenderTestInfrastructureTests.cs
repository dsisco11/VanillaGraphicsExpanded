using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Tests.GPU.Fixtures;
using VanillaGraphicsExpanded.Tests.GPU.Helpers;
using Xunit;

namespace VanillaGraphicsExpanded.Tests.GPU;

/// <summary>
/// Tests that verify the RenderTestBase infrastructure works correctly.
/// These tests validate render target creation, fullscreen quad rendering,
/// and pixel readback functionality using production GBuffer and DynamicTexture classes.
/// </summary>
[Collection("GPU")]
[Trait("Category", "GPU")]
public class RenderTestInfrastructureTests : RenderTestBase
{
    private readonly HeadlessGLFixture _fixture;
    private readonly ShaderTestHelper? _helper;

    public RenderTestInfrastructureTests(HeadlessGLFixture fixture) : base(fixture)
    {
        _fixture = fixture;

        if (_fixture.IsContextValid)
        {
            var shaderPath = Path.Combine(AppContext.BaseDirectory, "assets", "shaders");
            var includePath = Path.Combine(AppContext.BaseDirectory, "assets", "shaderincludes");

            if (Directory.Exists(shaderPath) && Directory.Exists(includePath))
            {
                _helper = new ShaderTestHelper(shaderPath, includePath);
            }
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _helper?.Dispose();
        }
        base.Dispose(disposing);
    }

    #region Render Target Tests

    [Fact]
    public void CreateRenderTarget_CreatesValidGBuffer()
    {
        EnsureContextValid();

        using var gBuffer = CreateRenderTarget(64, 64, PixelInternalFormat.Rgba16f);

        Assert.True(gBuffer.FboId > 0, "FBO ID should be valid");
        Assert.Equal(64, gBuffer.Width);
        Assert.Equal(64, gBuffer.Height);
        Assert.Equal(1, gBuffer.ColorAttachmentCount);
        Assert.True(gBuffer.GetColorTextureId(0) > 0, "Texture ID should be valid");
    }

    [Fact]
    public void CreateRenderTarget_DifferentFormats_Succeeds()
    {
        EnsureContextValid();

        var formats = new[]
        {
            PixelInternalFormat.Rgba8,
            PixelInternalFormat.Rgba16f,
            PixelInternalFormat.Rgba32f,
            PixelInternalFormat.Rg16f,
            PixelInternalFormat.R32f
        };

        foreach (var format in formats)
        {
            using var gBuffer = CreateRenderTarget(32, 32, format);
            Assert.True(gBuffer.FboId > 0, $"FBO should be valid for format {format}");
            AssertNoGLError($"CreateRenderTarget({format})");
        }
    }

    [Fact]
    public void CreateMRTRenderTarget_CreatesMultipleAttachments()
    {
        EnsureContextValid();

        using var gBuffer = CreateMRTRenderTarget(64, 64, 
            PixelInternalFormat.Rgba16f, 
            PixelInternalFormat.Rgba16f);

        Assert.True(gBuffer.FboId > 0, "FBO ID should be valid");
        Assert.Equal(2, gBuffer.ColorAttachmentCount);
        Assert.True(gBuffer.GetColorTextureId(0) > 0, "First texture ID should be valid");
        Assert.True(gBuffer.GetColorTextureId(1) > 0, "Second texture ID should be valid");
        Assert.NotEqual(gBuffer.GetColorTextureId(0), gBuffer.GetColorTextureId(1));
    }

    [Fact]
    public void GBuffer_BindAndUnbind_Works()
    {
        EnsureContextValid();

        using var gBuffer = CreateRenderTarget(64, 64, PixelInternalFormat.Rgba16f);

        gBuffer.Bind();
        AssertNoGLError("Bind");

        GBuffer.Unbind();
        AssertNoGLError("Unbind");
    }

    [Fact]
    public void GBuffer_Clear_Works()
    {
        EnsureContextValid();

        using var gBuffer = CreateRenderTarget(64, 64, PixelInternalFormat.Rgba16f);
        AssertNoGLError("CreateRenderTarget");

        gBuffer.Bind();
        AssertNoGLError("Bind");
        
        gBuffer.Clear(1.0f, 0.5f, 0.25f, 1.0f);
        AssertNoGLError("Clear");
        
        GBuffer.Unbind();
        AssertNoGLError("Unbind");

        // Verify cleared color
        var pixels = ReadPixelsFloat(gBuffer);
        Assert.NotEmpty(pixels);

        // Check first pixel (bottom-left)
        Assert.True(Math.Abs(pixels[0] - 1.0f) < 0.01f, $"Red should be ~1.0, got {pixels[0]}");
        Assert.True(Math.Abs(pixels[1] - 0.5f) < 0.01f, $"Green should be ~0.5, got {pixels[1]}");
        Assert.True(Math.Abs(pixels[2] - 0.25f) < 0.01f, $"Blue should be ~0.25, got {pixels[2]}");
        Assert.True(Math.Abs(pixels[3] - 1.0f) < 0.01f, $"Alpha should be ~1.0, got {pixels[3]}");
    }

    #endregion

    #region Fullscreen Quad Tests

    [Fact]
    public void RenderFullscreenQuad_WithSimpleShader_Succeeds()
    {
        EnsureContextValid();

        // Create a minimal shader that outputs UV coordinates as color
        var vertexSource = @"#version 330 core
layout(location = 0) in vec2 position;
layout(location = 1) in vec2 texCoord;
out vec2 vTexCoord;
void main() {
    gl_Position = vec4(position, 0.0, 1.0);
    vTexCoord = texCoord;
}";
        var fragmentSource = @"#version 330 core
in vec2 vTexCoord;
out vec4 fragColor;
void main() {
    fragColor = vec4(vTexCoord.x, vTexCoord.y, 0.0, 1.0);
}";

        // Compile shader using OpenTK
        var programId = CompileMinimalShader(vertexSource, fragmentSource);
        Assert.True(programId > 0, "Shader compilation should succeed");

        using var gBuffer = CreateRenderTarget(64, 64, PixelInternalFormat.Rgba16f);
        
        gBuffer.BindWithViewport();
        gBuffer.Clear(0f, 0f, 0f, 0f);

        RenderFullscreenQuad(programId);
        AssertNoGLError("RenderFullscreenQuad");

        GBuffer.Unbind();

        // Verify output - top-right corner should have high UV values
        var (r, g, _, _) = ReadPixel(gBuffer, 63, 63);
        Assert.True(r > 0.9f, $"Top-right red (U) should be ~1.0, got {r}");
        Assert.True(g > 0.9f, $"Top-right green (V) should be ~1.0, got {g}");

        // Bottom-left should have low UV values
        var (r2, g2, _, _) = ReadPixel(gBuffer, 0, 0);
        Assert.True(r2 < 0.1f, $"Bottom-left red (U) should be ~0.0, got {r2}");
        Assert.True(g2 < 0.1f, $"Bottom-left green (V) should be ~0.0, got {g2}");

        // Cleanup
        GL.DeleteProgram(programId);
    }

    #endregion

    #region Pixel Readback Tests

    [Fact]
    public void ReadPixelsFloat_ReturnsCorrectData()
    {
        EnsureContextValid();

        using var gBuffer = CreateRenderTarget(4, 4, PixelInternalFormat.Rgba16f);
        
        // Clear to a known color
        gBuffer.Bind();
        gBuffer.Clear(0.25f, 0.5f, 0.75f, 1.0f);
        GBuffer.Unbind();

        var pixels = ReadPixelsFloat(gBuffer);

        Assert.Equal(4 * 4 * 4, pixels.Length); // 4x4 pixels, 4 components each

        // Check a sample pixel
        const float tolerance = 0.01f;
        Assert.True(Math.Abs(pixels[0] - 0.25f) < tolerance, $"Red mismatch: {pixels[0]}");
        Assert.True(Math.Abs(pixels[1] - 0.5f) < tolerance, $"Green mismatch: {pixels[1]}");
        Assert.True(Math.Abs(pixels[2] - 0.75f) < tolerance, $"Blue mismatch: {pixels[2]}");
        Assert.True(Math.Abs(pixels[3] - 1.0f) < tolerance, $"Alpha mismatch: {pixels[3]}");
    }

    [Fact]
    public void ReadPixelsByte_ReturnsCorrectData()
    {
        EnsureContextValid();

        using var gBuffer = CreateRenderTarget(4, 4, PixelInternalFormat.Rgba8);
        
        // Clear to a known color (roughly 64, 128, 192, 255 in byte values)
        gBuffer.Bind();
        gBuffer.Clear(0.25f, 0.5f, 0.75f, 1.0f);
        GBuffer.Unbind();

        var pixels = ReadPixelsByte(gBuffer);

        Assert.Equal(4 * 4 * 4, pixels.Length);

        // Check first pixel (with some tolerance for rounding)
        Assert.True(Math.Abs(pixels[0] - 64) < 2, $"Red mismatch: {pixels[0]}");
        Assert.True(Math.Abs(pixels[1] - 128) < 2, $"Green mismatch: {pixels[1]}");
        Assert.True(Math.Abs(pixels[2] - 191) < 2, $"Blue mismatch: {pixels[2]}");
        Assert.Equal(255, pixels[3]); // Alpha should be exactly 255
    }

    [Fact]
    public void ReadPixel_ReturnsSinglePixel()
    {
        EnsureContextValid();

        using var gBuffer = CreateRenderTarget(8, 8, PixelInternalFormat.Rgba16f);
        gBuffer.Bind();
        gBuffer.Clear(0.1f, 0.2f, 0.3f, 0.4f);
        GBuffer.Unbind();

        var (r, g, b, a) = ReadPixel(gBuffer, 4, 4);

        const float tolerance = 0.01f;
        Assert.True(Math.Abs(r - 0.1f) < tolerance, $"Red mismatch: {r}");
        Assert.True(Math.Abs(g - 0.2f) < tolerance, $"Green mismatch: {g}");
        Assert.True(Math.Abs(b - 0.3f) < tolerance, $"Blue mismatch: {b}");
        Assert.True(Math.Abs(a - 0.4f) < tolerance, $"Alpha mismatch: {a}");
    }

    #endregion

    #region Utility Method Tests

    [Fact]
    public void CheckGLError_ReturnsNoError_WhenNoError()
    {
        EnsureContextValid();

        // Clear any pending errors first
        while (GL.GetError() != ErrorCode.NoError) { }

        var error = CheckGLError();
        Assert.Equal(ErrorCode.NoError, error);
    }

    [Fact]
    public void AssertNoGLError_DoesNotThrow_WhenNoError()
    {
        EnsureContextValid();

        // Clear any pending errors first
        while (GL.GetError() != ErrorCode.NoError) { }

        var exception = Record.Exception(() => AssertNoGLError("test context"));
        Assert.Null(exception);
    }

    #endregion

    #region Helper Methods

    /// <summary>
    /// Compiles a minimal shader for testing purposes using OpenTK.
    /// </summary>
    private int CompileMinimalShader(string vertexSource, string fragmentSource)
    {
        // Compile vertex shader
        var vertexShader = GL.CreateShader(ShaderType.VertexShader);
        GL.ShaderSource(vertexShader, vertexSource);
        GL.CompileShader(vertexShader);
        
        GL.GetShader(vertexShader, ShaderParameter.CompileStatus, out int vStatus);
        if (vStatus == 0)
        {
            var log = GL.GetShaderInfoLog(vertexShader);
            GL.DeleteShader(vertexShader);
            throw new InvalidOperationException($"Vertex shader compile error: {log}");
        }

        // Compile fragment shader
        var fragmentShader = GL.CreateShader(ShaderType.FragmentShader);
        GL.ShaderSource(fragmentShader, fragmentSource);
        GL.CompileShader(fragmentShader);
        
        GL.GetShader(fragmentShader, ShaderParameter.CompileStatus, out int fStatus);
        if (fStatus == 0)
        {
            var log = GL.GetShaderInfoLog(fragmentShader);
            GL.DeleteShader(vertexShader);
            GL.DeleteShader(fragmentShader);
            throw new InvalidOperationException($"Fragment shader compile error: {log}");
        }

        // Link program
        var program = GL.CreateProgram();
        GL.AttachShader(program, vertexShader);
        GL.AttachShader(program, fragmentShader);
        GL.LinkProgram(program);

        GL.GetProgram(program, GetProgramParameterName.LinkStatus, out int lStatus);
        if (lStatus == 0)
        {
            var log = GL.GetProgramInfoLog(program);
            GL.DeleteShader(vertexShader);
            GL.DeleteShader(fragmentShader);
            GL.DeleteProgram(program);
            throw new InvalidOperationException($"Program link error: {log}");
        }

        // Cleanup shaders (they're now part of the program)
        GL.DeleteShader(vertexShader);
        GL.DeleteShader(fragmentShader);

        return program;
    }

    #endregion
}
