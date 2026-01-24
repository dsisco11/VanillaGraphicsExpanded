using System;
using System.Collections.Generic;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.Rendering;

namespace VanillaGraphicsExpanded.Tests.GPU.Fixtures;

/// <summary>
/// Abstract base class for render tests that need to create render targets,
/// render fullscreen quads, and read back pixel data for verification.
/// Uses the production GBuffer and DynamicTexture classes.
/// </summary>
/// <remarks>
/// Usage:
/// <code>
/// [Collection("GPU")]
/// [Trait("Category", "GPU")]
/// public class MyRenderTests : RenderTestBase
/// {
///     public MyRenderTests(HeadlessGLFixture fixture) : base(fixture) { }
///     
///     [Fact]
///     public void MyTest()
///     {
///         EnsureContextValid();
///         using var rt = CreateRenderTarget(64, 64, PixelInternalFormat.Rgba16f);
///         // ... render and verify ...
///     }
/// }
/// </code>
/// </remarks>
public abstract class RenderTestBase : IDisposable
{
    private readonly HeadlessGLFixture _fixture;
    
    // Fullscreen quad VAO/VBO resources - lazy initialized
    private int _quadVao;
    private int _quadVbo;
    private bool _quadInitialized;
    private bool _disposed;

    /// <summary>
    /// Creates a new RenderTestBase instance.
    /// </summary>
    /// <param name="fixture">The HeadlessGLFixture providing the GL context.</param>
    protected RenderTestBase(HeadlessGLFixture fixture)
    {
        _fixture = fixture ?? throw new ArgumentNullException(nameof(fixture));
    }

    /// <summary>
    /// Ensures the GL context is valid; throws skip exception if not.
    /// Also drains any leftover GL errors from previous operations.
    /// </summary>
    protected void EnsureContextValid()
    {
        _fixture.EnsureContextValid();
        
        // Drain any leftover GL errors so each test starts clean
        while (GL.GetError() != ErrorCode.NoError) { }
    }

    #region Render Target Management

    /// <summary>
    /// Creates a render target (GBuffer with color attachment) for testing.
    /// </summary>
    /// <param name="width">Width in pixels.</param>
    /// <param name="height">Height in pixels.</param>
    /// <param name="format">Internal format (e.g., PixelInternalFormat.Rgba16f).</param>
    /// <returns>A disposable GBuffer instance that owns its texture.</returns>
    protected GpuFramebuffer CreateRenderTarget(int width, int height, PixelInternalFormat format)
    {
        EnsureContextValid();
        var texture = DynamicTexture2D.Create(width, height, format);
        var gBuffer = GpuFramebuffer.CreateSingle(texture, ownsTextures: true);
        return gBuffer ?? throw new InvalidOperationException("Failed to create GBuffer");
    }

    /// <summary>
    /// Creates a render target with multiple render targets (MRT).
    /// </summary>
    /// <param name="width">Width in pixels.</param>
    /// <param name="height">Height in pixels.</param>
    /// <param name="formats">Internal formats for each color attachment.</param>
    /// <returns>A disposable GBuffer instance that owns its textures.</returns>
    protected GpuFramebuffer CreateMRTRenderTarget(int width, int height, params PixelInternalFormat[] formats)
    {
        EnsureContextValid();
        var textures = new DynamicTexture2D[formats.Length];
        for (int i = 0; i < formats.Length; i++)
        {
            textures[i] = DynamicTexture2D.Create(width, height, formats[i]);
        }
        var gBuffer = GpuFramebuffer.CreateMRT(textures, ownsTextures: true);
        return gBuffer ?? throw new InvalidOperationException("Failed to create MRT GBuffer");
    }

    #endregion

    #region Fullscreen Quad Rendering

    /// <summary>
    /// Initializes the fullscreen quad VAO if not already initialized.
    /// </summary>
    private void EnsureQuadInitialized()
    {
        if (_quadInitialized)
            return;

        // Fullscreen quad vertices: position (x,y) and texcoord (u,v)
        // Two triangles covering the screen from -1 to 1 in NDC
        float[] quadVertices =
        [
            // First triangle
            -1.0f, -1.0f,  0.0f, 0.0f,  // bottom-left
             1.0f, -1.0f,  1.0f, 0.0f,  // bottom-right
             1.0f,  1.0f,  1.0f, 1.0f,  // top-right
            // Second triangle
            -1.0f, -1.0f,  0.0f, 0.0f,  // bottom-left
             1.0f,  1.0f,  1.0f, 1.0f,  // top-right
            -1.0f,  1.0f,  0.0f, 1.0f,  // top-left
        ];

        _quadVao = GL.GenVertexArray();
        _quadVbo = GL.GenBuffer();

        GL.BindVertexArray(_quadVao);

        GL.BindBuffer(BufferTarget.ArrayBuffer, _quadVbo);
        GL.BufferData(BufferTarget.ArrayBuffer, quadVertices.Length * sizeof(float), quadVertices, BufferUsageHint.StaticDraw);

        // Position attribute (location 0)
        GL.EnableVertexAttribArray(0);
        GL.VertexAttribPointer(0, 2, VertexAttribPointerType.Float, false, 4 * sizeof(float), 0);

        // TexCoord attribute (location 1)
        GL.EnableVertexAttribArray(1);
        GL.VertexAttribPointer(1, 2, VertexAttribPointerType.Float, false, 4 * sizeof(float), 2 * sizeof(float));

        GL.BindVertexArray(0);
        GL.BindBuffer(BufferTarget.ArrayBuffer, 0);

        _quadInitialized = true;
    }

    /// <summary>
    /// Renders a fullscreen quad using the currently bound shader program.
    /// </summary>
    /// <param name="programId">The shader program to use.</param>
    protected void RenderFullscreenQuad(int programId)
    {
        EnsureContextValid();
        EnsureQuadInitialized();

        GL.UseProgram(programId);
        GL.BindVertexArray(_quadVao);
        GL.DrawArrays(PrimitiveType.Triangles, 0, 6);
        GL.BindVertexArray(0);
        GL.UseProgram(0);
    }

    /// <summary>
    /// Renders a fullscreen quad with the shader already bound.
    /// Assumes the caller has already called GL.UseProgram().
    /// </summary>
    protected void RenderFullscreenQuad()
    {
        EnsureContextValid();
        EnsureQuadInitialized();

        GL.BindVertexArray(_quadVao);
        GL.DrawArrays(PrimitiveType.Triangles, 0, 6);
        GL.BindVertexArray(0);
    }

    #endregion

    #region Pixel Readback

    /// <summary>
    /// Reads all pixels from the specified GBuffer as float arrays.
    /// </summary>
    /// <param name="gBuffer">The GBuffer to read from.</param>
    /// <returns>Array of RGBA float values (4 floats per pixel).</returns>
    protected float[] ReadPixelsFloat(GpuFramebuffer gBuffer)
    {
        EnsureContextValid();

        GL.BindFramebuffer(FramebufferTarget.ReadFramebuffer, gBuffer.FboId);
        
        var pixels = new float[gBuffer.Width * gBuffer.Height * 4];
        GL.ReadPixels(0, 0, gBuffer.Width, gBuffer.Height, 
            OpenTK.Graphics.OpenGL.PixelFormat.Rgba, PixelType.Float, pixels);

        GL.BindFramebuffer(FramebufferTarget.ReadFramebuffer, 0);
        return pixels;
    }

    /// <summary>
    /// Reads all pixels from the specified GBuffer as byte arrays.
    /// </summary>
    /// <param name="gBuffer">The GBuffer to read from.</param>
    /// <returns>Array of RGBA byte values (4 bytes per pixel).</returns>
    protected byte[] ReadPixelsByte(GpuFramebuffer gBuffer)
    {
        EnsureContextValid();

        GL.BindFramebuffer(FramebufferTarget.ReadFramebuffer, gBuffer.FboId);
        
        var pixels = new byte[gBuffer.Width * gBuffer.Height * 4];
        GL.ReadPixels(0, 0, gBuffer.Width, gBuffer.Height, 
            OpenTK.Graphics.OpenGL.PixelFormat.Rgba, PixelType.UnsignedByte, pixels);

        GL.BindFramebuffer(FramebufferTarget.ReadFramebuffer, 0);
        return pixels;
    }

    /// <summary>
    /// Reads a single pixel from the specified GBuffer.
    /// </summary>
    /// <param name="gBuffer">The GBuffer to read from.</param>
    /// <param name="x">X coordinate.</param>
    /// <param name="y">Y coordinate.</param>
    /// <returns>RGBA float values for the pixel.</returns>
    protected (float R, float G, float B, float A) ReadPixel(GpuFramebuffer gBuffer, int x, int y)
    {
        EnsureContextValid();

        GL.BindFramebuffer(FramebufferTarget.ReadFramebuffer, gBuffer.FboId);
        
        float[] pixel = new float[4];
        GL.ReadPixels(x, y, 1, 1, OpenTK.Graphics.OpenGL.PixelFormat.Rgba, PixelType.Float, pixel);

        GL.BindFramebuffer(FramebufferTarget.ReadFramebuffer, 0);
        return (pixel[0], pixel[1], pixel[2], pixel[3]);
    }

    #endregion

    #region Utility Methods

    /// <summary>
    /// Checks for GL errors and returns the error code.
    /// </summary>
    /// <returns>The GL error code, or NoError if no error.</returns>
    protected ErrorCode CheckGLError()
    {
        return GL.GetError();
    }

    /// <summary>
    /// Asserts that there are no GL errors pending.
    /// Drains all queued errors (GL can queue multiple).
    /// </summary>
    /// <param name="context">Context message for error reporting.</param>
    protected void AssertNoGLError(string context = "")
    {
        var errors = new List<ErrorCode>();
        ErrorCode error;
        while ((error = GL.GetError()) != ErrorCode.NoError)
        {
            errors.Add(error);
        }
        
        if (errors.Count > 0)
        {
            var errorList = string.Join(", ", errors);
            var message = string.IsNullOrEmpty(context) 
                ? $"GL Error(s): {errorList}" 
                : $"GL Error(s) ({context}): {errorList}";
            throw new InvalidOperationException(message);
        }
    }

    /// <summary>
    /// Sets a uniform float value on the specified program.
    /// </summary>
    protected void SetUniform(int programId, string name, float value)
    {
        var location = GL.GetUniformLocation(programId, name);
        if (location >= 0)
        {
            GL.UseProgram(programId);
            GL.Uniform1(location, value);
        }
    }

    /// <summary>
    /// Sets a uniform vec2 value on the specified program.
    /// </summary>
    protected void SetUniform(int programId, string name, float x, float y)
    {
        var location = GL.GetUniformLocation(programId, name);
        if (location >= 0)
        {
            GL.UseProgram(programId);
            GL.Uniform2(location, x, y);
        }
    }

    /// <summary>
    /// Sets a uniform vec3 value on the specified program.
    /// </summary>
    protected void SetUniform(int programId, string name, float x, float y, float z)
    {
        var location = GL.GetUniformLocation(programId, name);
        if (location >= 0)
        {
            GL.UseProgram(programId);
            GL.Uniform3(location, x, y, z);
        }
    }

    /// <summary>
    /// Sets a uniform vec4 value on the specified program.
    /// </summary>
    protected void SetUniform(int programId, string name, float x, float y, float z, float w)
    {
        var location = GL.GetUniformLocation(programId, name);
        if (location >= 0)
        {
            GL.UseProgram(programId);
            GL.Uniform4(location, x, y, z, w);
        }
    }

    /// <summary>
    /// Sets a uniform int value on the specified program.
    /// </summary>
    protected void SetUniform(int programId, string name, int value)
    {
        var location = GL.GetUniformLocation(programId, name);
        if (location >= 0)
        {
            GL.UseProgram(programId);
            GL.Uniform1(location, value);
        }
    }

    /// <summary>
    /// Binds a texture to a texture unit and sets the corresponding sampler uniform.
    /// </summary>
    protected void BindTexture(int programId, string uniformName, int textureUnit, int textureId)
    {
        GL.ActiveTexture(TextureUnit.Texture0 + textureUnit);
        GL.BindTexture(TextureTarget.Texture2D, textureId);
        SetUniform(programId, uniformName, textureUnit);
    }

    #endregion

    #region IDisposable

    /// <summary>
    /// Disposes GPU resources used by the render test base.
    /// </summary>
    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    /// <summary>
    /// Override this to dispose additional resources in derived classes.
    /// </summary>
    protected virtual void Dispose(bool disposing)
    {
        if (_disposed)
            return;

        if (disposing && _quadInitialized && _fixture.IsContextValid)
        {
            GL.DeleteVertexArray(_quadVao);
            GL.DeleteBuffer(_quadVbo);
            _quadVao = 0;
            _quadVbo = 0;
            _quadInitialized = false;
        }

        _disposed = true;
    }

    #endregion
}
