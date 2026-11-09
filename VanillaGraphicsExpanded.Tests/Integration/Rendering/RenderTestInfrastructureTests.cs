using VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;
using VanillaGraphicsExpanded.Rendering.RHI;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Tests.Support.GPU.Shaders;
using VanillaGraphicsExpanded.Rendering.Pipeline;
using VanillaGraphicsExpanded.Rendering.Pipeline.Descriptions;
using Xunit;

using VanillaGraphicsExpanded.Tests.Support.GPU.Native;
namespace VanillaGraphicsExpanded.Tests.Integration.Rendering;

/// <summary>
/// Tests that verify the RenderTestBase infrastructure works correctly.
/// These tests validate render target creation, fullscreen quad rendering,
/// and pixel readback functionality using production GBuffer and DynamicTexture classes.
/// </summary>
[Collection("GPU")]
[Trait("Category", "GpuIntegration")]
public class RenderTestInfrastructureTests : RenderTestBase
{
    #region Public API

    /// <summary>Uses the shared headless rendering context.</summary>
    public RenderTestInfrastructureTests(HeadlessGLFixture fixture) : base(fixture) { }

    #region Render Target Tests

    /// <summary>Creates a valid single color target with the requested dimensions and format.</summary>
    [Fact]
    public void CreateRenderTarget_CreatesValidGBuffer()
    {
        // Arrange
        EnsureContextValid();

        // Act
        using var gBuffer = CreateRenderTarget(64, 64, TextureFormat.Rgba16f);

        // Assert
        Assert.True(gBuffer.FboId > 0, "FBO ID should be valid");
        Assert.Equal(64, gBuffer.Width);
        Assert.Equal(64, gBuffer.Height);
        Assert.Equal(1, gBuffer.ColorAttachmentCount);
        Assert.True(gBuffer.GetColorTextureId(0) > 0, "Texture ID should be valid");
    }

    /// <summary>Creates render targets for each supported color format in the scenario.</summary>
    [Fact]
    public void CreateRenderTarget_DifferentFormats_Succeeds()
    {
        // Arrange
        EnsureContextValid();

        var formats = new[]
        {
            TextureFormat.Rgba8,
            TextureFormat.Rgba16f,
            TextureFormat.Rgba32f,
            TextureFormat.Rg16f,
            TextureFormat.R32f
        };

        // Act and assert each format independently so a failed allocation identifies its format.
        foreach (var format in formats)
        {
            using var gBuffer = CreateRenderTarget(32, 32, format);
            Assert.True(gBuffer.FboId > 0, $"FBO should be valid for format {format}");
            AssertNoGLError($"CreateRenderTarget({format})");
        }
    }

    /// <summary>Creates two distinct color attachments for a multiple render target.</summary>
    [Fact]
    public void CreateMRTRenderTarget_CreatesMultipleAttachments()
    {
        // Arrange
        EnsureContextValid();

        // Act
        using var gBuffer = CreateMRTRenderTarget(64, 64,
            TextureFormat.Rgba16f,
            TextureFormat.Rgba16f);

        // Assert
        Assert.True(gBuffer.FboId > 0, "FBO ID should be valid");
        Assert.Equal(2, gBuffer.ColorAttachmentCount);
        Assert.True(gBuffer.GetColorTextureId(0) > 0, "First texture ID should be valid");
        Assert.True(gBuffer.GetColorTextureId(1) > 0, "Second texture ID should be valid");
        Assert.NotEqual(gBuffer.GetColorTextureId(0), gBuffer.GetColorTextureId(1));
    }

    /// <summary>Binds and unbinds a valid GBuffer without producing OpenGL errors.</summary>
    [Fact]
    public void GBuffer_BindAndUnbind_Works()
    {
        // Arrange
        EnsureContextValid();

        using var gBuffer = CreateRenderTarget(64, 64, TextureFormat.Rgba16f);
        // Act
        gBuffer.Bind();
        // Assert
        AssertNoGLError("Bind");

        // Act
        GpuFramebuffer.Unbind();
        // Assert
        AssertNoGLError("Unbind");
    }

    /// <summary>Clears a GBuffer attachment and reads back the requested RGBA values.</summary>
    [Fact]
    public void GBuffer_Clear_Works()
    {
        // Arrange
        EnsureContextValid();

        using var gBuffer = CreateRenderTarget(64, 64, TextureFormat.Rgba16f);
        AssertNoGLError("CreateRenderTarget");

        // Act
        gBuffer.Bind();
        AssertNoGLError("Bind");

        FramebufferTestSetup.ClearAttachments(gBuffer, 1.0f, 0.5f, 0.25f, 1.0f);
        AssertNoGLError("Clear");

        GpuFramebuffer.Unbind();
        AssertNoGLError("Unbind");

        // Assert the clear reached GPU storage by reading one pixel after unbinding.
        var pixels = gBuffer[0].ReadPixelsRegion(0,0,1,1);
        Assert.NotEmpty(pixels);

        // Check first pixel (bottom-left)
        Assert.True(Math.Abs(pixels[0] - 1.0f) < 0.01f, $"Red should be ~1.0, got {pixels[0]}");
        Assert.True(Math.Abs(pixels[1] - 0.5f) < 0.01f, $"Green should be ~0.5, got {pixels[1]}");
        Assert.True(Math.Abs(pixels[2] - 0.25f) < 0.01f, $"Blue should be ~0.25, got {pixels[2]}");
        Assert.True(Math.Abs(pixels[3] - 1.0f) < 0.01f, $"Alpha should be ~1.0, got {pixels[3]}");
    }

    #endregion

    #region Fullscreen Quad Tests

    /// <summary>Typed fullscreen submission preserves the original two-triangle UV output across the target.</summary>
    [Fact]
    public void RenderFullscreenQuad_WithSimpleShader_Succeeds()
    {
        // Arrange the target, shader inputs, geometry, and render-pass description.
        EnsureContextValid();
        using var gBuffer = CreateRenderTarget(64, 64, TextureFormat.Rgba16f);

        using var programs = new ComponentShaderPrograms();
        var program = programs.Create<RenderInfrastructureFixtureProgram>();
        var commands = RHICommandContext.Current;
        var layout = new RHIVertexLayout([
            new(0, 2, VertexComponentFormat.Float, VertexInterpretation.Floating, 0, 0, 16),
            new(1, 2, VertexComponentFormat.Float, VertexInterpretation.Floating, 0, 8, 16)]);
        using var vertices = commands.CreateVertexBuffer(new() { Role = BufferRole.Vertex, Mode = ResourceStorageMode.Mutable, Capacity = 96, Usage = BufferUsage.StaticDraw }).RequireResource();
        // Preserve RenderTestBase's authored two-triangle positions and UVs exactly.
        vertices.UploadData(new float[] {
            -1, -1, 0, 0, 1, -1, 1, 0, 1, 1, 1, 1,
            -1, -1, 0, 0, 1, 1, 1, 1, -1, 1, 0, 1 });
        using var geometry = new ArrayGraphicsGeometry(layout, PrimitiveTopology.Triangles,
            new Dictionary<int, GpuVbo> { [0] = vertices });
        var pass = FramebufferTestSetup.DescribeAttachments(gBuffer,
            [new(AttachmentIndex: 0, Load: AttachmentLoad.Clear, Clear: ColorClearValue.Float(0, 0, 0, 0))]);
        using var pipeline = commands.CreateGraphicsPipeline(new(program.Shader, layout,
            commands.DescribeRenderTargets(pass), DynamicPipelineState.Viewport)).RequireResource();
        using var boundary = commands.EnterBoundary(new("Tests.RenderInfrastructure.Fullscreen", true,
            graphics: [pipeline], operations: BoundaryOperations.RenderPasses));
        // Act: submit the authored two-triangle quad through the typed render boundary.
        boundary.Run(() =>
        {
            commands.BeginRenderPass(pass);
            commands.SetPipeline(pipeline);
            commands.SetDynamicState(new() { Viewport = commands.PassViewport });
            commands.BindResources(program.CaptureBindings());
            commands.Draw(geometry.Inputs, new(0, 6));
            commands.EndRenderPass();
        });
        AssertNoGLError("RenderFullscreenQuad");

        // Assert interpolated UVs at opposite corners retain the expected orientation.
        var (r, g, _, _) = ReadPixel(gBuffer, 63, 63);
        Assert.True(r > 0.9f, $"Top-right red (U) should be ~1.0, got {r}");
        Assert.True(g > 0.9f, $"Top-right green (V) should be ~1.0, got {g}");

        // Bottom-left should have low UV values
        var (r2, g2, _, _) = ReadPixel(gBuffer, 0, 0);
        Assert.True(r2 < 0.1f, $"Bottom-left red (U) should be ~0.0, got {r2}");
        Assert.True(g2 < 0.1f, $"Bottom-left green (V) should be ~0.0, got {g2}");

    }

    #endregion

    #region Pixel Readback Tests

    /// <summary>Reads four floating-point color components from a cleared RGBA16F target.</summary>
    [Fact]
    public void ReadPixelsFloat_ReturnsCorrectData()
    {
        // Arrange
        EnsureContextValid();

        using var gBuffer = CreateRenderTarget(1, 1, TextureFormat.Rgba16f);

        // Act: clear the target, then read its single RGBA pixel.
        gBuffer.Bind();
        FramebufferTestSetup.ClearAttachments(gBuffer, 0.25f, 0.5f, 0.75f, 1.0f);
        GpuFramebuffer.Unbind();

        var pixels = ReadPixelsFloat(gBuffer);

        // Assert
        Assert.Equal(4,pixels.Length); // One known RGBA tuple is sufficient for this readback API subject.

        // Check a sample pixel
        const float tolerance = 0.01f;
        Assert.True(Math.Abs(pixels[0] - 0.25f) < tolerance, $"Red mismatch: {pixels[0]}");
        Assert.True(Math.Abs(pixels[1] - 0.5f) < tolerance, $"Green mismatch: {pixels[1]}");
        Assert.True(Math.Abs(pixels[2] - 0.75f) < tolerance, $"Blue mismatch: {pixels[2]}");
        Assert.True(Math.Abs(pixels[3] - 1.0f) < tolerance, $"Alpha mismatch: {pixels[3]}");
    }

    /// <summary>Reads normalized byte color components from a cleared RGBA8 target.</summary>
    [Fact]
    public void ReadPixelsByte_ReturnsCorrectData()
    {
        // Arrange
        EnsureContextValid();

        using var gBuffer = CreateRenderTarget(1, 1, TextureFormat.Rgba8);

        // Act: clear to values with known byte representations and read them back.
        gBuffer.Bind();
        FramebufferTestSetup.ClearAttachments(gBuffer, 0.25f, 0.5f, 0.75f, 1.0f);
        GpuFramebuffer.Unbind();

        var pixels = ReadPixelsByte(gBuffer);

        // Assert
        Assert.Equal(4, pixels.Length);

        // Check first pixel (with some tolerance for rounding)
        Assert.True(Math.Abs(pixels[0] - 64) < 2, $"Red mismatch: {pixels[0]}");
        Assert.True(Math.Abs(pixels[1] - 128) < 2, $"Green mismatch: {pixels[1]}");
        Assert.True(Math.Abs(pixels[2] - 191) < 2, $"Blue mismatch: {pixels[2]}");
        Assert.Equal(255, pixels[3]); // Alpha should be exactly 255
    }

    /// <summary>Reads one pixel at the requested coordinate from the cleared target.</summary>
    [Fact]
    public void ReadPixel_ReturnsSinglePixel()
    {
        // Arrange
        EnsureContextValid();

        using var gBuffer = CreateRenderTarget(8, 8, TextureFormat.Rgba16f);
        gBuffer.Bind();
        FramebufferTestSetup.ClearAttachments(gBuffer, 0.1f, 0.2f, 0.3f, 0.4f);
        GpuFramebuffer.Unbind();

        // Act
        var (r, g, b, a) = ReadPixel(gBuffer, 4, 4);

        // Assert
        const float tolerance = 0.01f;
        Assert.True(Math.Abs(r - 0.1f) < tolerance, $"Red mismatch: {r}");
        Assert.True(Math.Abs(g - 0.2f) < tolerance, $"Green mismatch: {g}");
        Assert.True(Math.Abs(b - 0.3f) < tolerance, $"Blue mismatch: {b}");
        Assert.True(Math.Abs(a - 0.4f) < tolerance, $"Alpha mismatch: {a}");
    }

    #endregion

    #region Utility Method Tests

    /// <summary>Returns no error when the current OpenGL context has no pending error.</summary>
    [Fact]
    public void CheckGLError_ReturnsNoError_WhenNoError()
    {
        // Arrange
        EnsureContextValid();

        // Clear any pending errors first
        while (GL.GetError() != ErrorCode.NoError) { }

        // Act
        var error = CheckGLError();
        // Assert
        Assert.Equal(ErrorCode.NoError, error);
    }

    /// <summary>Does not throw when the current OpenGL context has no pending error.</summary>
    [Fact]
    public void AssertNoGLError_DoesNotThrow_WhenNoError()
    {
        // Arrange
        EnsureContextValid();

        // Clear any pending errors first
        while (GL.GetError() != ErrorCode.NoError) { }

        // Act
        var exception = Record.Exception(() => AssertNoGLError("test context"));
        // Assert
        Assert.Null(exception);
    }

    #endregion
    #endregion

}
        // Assert
