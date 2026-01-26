using System;
using System.Collections.Generic;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.Rendering;

namespace VanillaGraphicsExpanded.Tests.GPU.Helpers;

/// <summary>
/// Framework for functional GPU shader tests.
/// Provides helpers for creating test textures, render targets, and executing shader passes.
/// </summary>
/// <remarks>
/// Usage:
/// <code>
/// using var framework = new ShaderTestFramework();
/// 
/// // Create input textures with test data
/// using var depthTex = framework.CreateTexture(4, 4, PixelInternalFormat.R32f, depthData);
/// using var normalTex = framework.CreateTexture(4, 4, PixelInternalFormat.Rgba16f, normalData);
/// 
/// // Create output render target
/// using var outputGBuffer = framework.CreateTestGBuffer(2, 2, PixelInternalFormat.Rgba16f, 2);
/// 
/// // Bind inputs and render
/// depthTex.Bind(0);
/// normalTex.Bind(1);
/// outputGBuffer.BindWithViewport();
/// framework.RenderQuad(programId);
/// 
/// // Read back and validate
/// var result = outputGBuffer[0].ReadPixels();
/// </code>
/// </remarks>
public sealed class ShaderTestFramework : IDisposable
{
    #region Fields

    private int _quadVao;
    private int _quadVbo;
    private bool _quadInitialized;
    private bool _isDisposed;
    private readonly List<IDisposable> _managedResources = [];
    private static readonly GlPipelineDesc FullscreenPassPso = CreateFullscreenPassPso();

    #endregion

    private static GlPipelineDesc CreateFullscreenPassPso()
    {
        // Match typical runtime fullscreen compute/post-process defaults (no depth, no blend, no cull, no scissor).
        var defaultMask =
            GlPipelineStateMask.From(GlPipelineStateId.DepthTestEnable)
                .With(GlPipelineStateId.DepthFunc)
                .With(GlPipelineStateId.BlendEnable)
                .With(GlPipelineStateId.BlendFunc)
                .With(GlPipelineStateId.CullFaceEnable)
                .With(GlPipelineStateId.ScissorTestEnable)
                .With(GlPipelineStateId.ColorMask);

        var nonDefaultMask = GlPipelineStateMask.From(GlPipelineStateId.DepthWriteMask);

        return new GlPipelineDesc(
            defaultMask: defaultMask,
            nonDefaultMask: nonDefaultMask,
            depthWriteMask: false,
            name: "Tests.FullscreenPass");
    }

    #region Texture Creation

    /// <summary>
    /// Creates a texture with initial data for use as shader input.
    /// The texture is tracked for automatic disposal.
    /// </summary>
    /// <param name="width">Texture width in pixels.</param>
    /// <param name="height">Texture height in pixels.</param>
    /// <param name="format">Internal pixel format.</param>
    /// <param name="data">Float array containing pixel data.</param>
    /// <param name="filter">Filtering mode. Default is Nearest for test precision.</param>
    /// <returns>A new DynamicTexture with uploaded data.</returns>
    public DynamicTexture2D CreateTexture(
        int width,
        int height,
        PixelInternalFormat format,
        float[] data,
        TextureFilterMode filter = TextureFilterMode.Nearest)
    {
        var texture = DynamicTexture2D.CreateWithData(width, height, format, data, filter);
        _managedResources.Add(texture);
        return texture;
    }

    /// <summary>
    /// Creates an empty texture for use as shader input.
    /// The texture is tracked for automatic disposal.
    /// </summary>
    /// <param name="width">Texture width in pixels.</param>
    /// <param name="height">Texture height in pixels.</param>
    /// <param name="format">Internal pixel format.</param>
    /// <param name="filter">Filtering mode. Default is Nearest.</param>
    /// <returns>A new DynamicTexture.</returns>
    public DynamicTexture2D CreateTexture(
        int width,
        int height,
        PixelInternalFormat format,
        TextureFilterMode filter = TextureFilterMode.Nearest)
    {
        var texture = DynamicTexture2D.Create(width, height, format, filter);
        _managedResources.Add(texture);
        return texture;
    }

    #endregion

    #region GBuffer Creation

    /// <summary>
    /// Creates a GBuffer with multiple render targets for shader output.
    /// All attachments use the same format and dimensions.
    /// The GBuffer and its textures are tracked for automatic disposal.
    /// </summary>
    /// <param name="width">Render target width in pixels.</param>
    /// <param name="height">Render target height in pixels.</param>
    /// <param name="format">Internal pixel format for all attachments.</param>
    /// <param name="attachmentCount">Number of color attachments (MRT).</param>
    /// <returns>A new GBuffer with the specified attachments.</returns>
    /// <exception cref="InvalidOperationException">Thrown if GBuffer creation fails.</exception>
    public GpuFramebuffer CreateTestGBuffer(
        int width,
        int height,
        PixelInternalFormat format,
        int attachmentCount = 1)
    {
        var textures = new DynamicTexture2D[attachmentCount];
        for (int i = 0; i < attachmentCount; i++)
        {
            textures[i] = DynamicTexture2D.Create(width, height, format);
        }

        var gBuffer = GpuFramebuffer.CreateMRT(textures, depthTexture: null, ownsTextures: true);
        if (gBuffer == null)
        {
            // Clean up textures if GBuffer creation failed
            foreach (var tex in textures)
                tex.Dispose();
            throw new InvalidOperationException("Failed to create GBuffer");
        }

        _managedResources.Add(gBuffer);
        return gBuffer;
    }

    /// <summary>
    /// Creates a GBuffer with multiple render targets using different formats per attachment.
    /// The GBuffer and its textures are tracked for automatic disposal.
    /// </summary>
    /// <param name="width">Render target width in pixels.</param>
    /// <param name="height">Render target height in pixels.</param>
    /// <param name="formats">Array of internal pixel formats, one per attachment.</param>
    /// <returns>A new GBuffer with the specified attachments.</returns>
    /// <exception cref="InvalidOperationException">Thrown if GBuffer creation fails.</exception>
    public GpuFramebuffer CreateTestGBuffer(
        int width,
        int height,
        params PixelInternalFormat[] formats)
    {
        var textures = new DynamicTexture2D[formats.Length];
        for (int i = 0; i < formats.Length; i++)
        {
            textures[i] = DynamicTexture2D.Create(width, height, formats[i]);
        }

        var gBuffer = GpuFramebuffer.CreateMRT(textures, depthTexture: null, ownsTextures: true);
        if (gBuffer == null)
        {
            foreach (var tex in textures)
                tex.Dispose();
            throw new InvalidOperationException("Failed to create GBuffer");
        }

        _managedResources.Add(gBuffer);
        return gBuffer;
    }

    #endregion

    #region Rendering

    /// <summary>
    /// Renders a fullscreen quad using the specified shader program.
    /// The quad covers clip space (-1,-1) to (1,1).
    /// Assumes the target GBuffer is already bound.
    /// </summary>
    /// <param name="programId">Compiled and linked shader program ID.</param>
    public void RenderQuad(int programId)
    {
        EnsureQuadInitialized();

        GlStateCache.Current.Apply(FullscreenPassPso);
        GlStateCache.Current.UseProgram(programId);
        GlStateCache.Current.BindVertexArray(_quadVao);
        GL.DrawArrays(PrimitiveType.Triangles, 0, 3);
        GlStateCache.Current.UnbindVertexArray();
        GlStateCache.Current.UnbindProgram();
    }

    /// <summary>
    /// Renders a fullscreen quad with explicit state setup.
    /// Binds the GBuffer, sets viewport, clears, then renders.
    /// </summary>
    /// <param name="programId">Compiled and linked shader program ID.</param>
    /// <param name="target">Target GBuffer to render to.</param>
    /// <param name="clearColor">Optional clear color (default: black with alpha 0).</param>
    public void RenderQuadTo(int programId, GpuFramebuffer target, (float r, float g, float b, float a)? clearColor = null)
    {
        target.BindWithViewport();

        GlStateCache.Current.Apply(FullscreenPassPso);

        var (r, g, b, a) = clearColor ?? (0f, 0f, 0f, 0f);
        GL.ClearColor(r, g, b, a);
        GL.Clear(ClearBufferMask.ColorBufferBit);

        RenderQuad(programId);

        GpuFramebuffer.Unbind();
    }

    /// <summary>
    /// Initializes the fullscreen quad VAO/VBO on first use.
    /// Uses a single triangle that covers the entire clip space.
    /// </summary>
    private void EnsureQuadInitialized()
    {
        if (_quadInitialized)
            return;

        // Fullscreen triangle vertices (clip space)
        // This triangle covers (-1,-1) to (1,1) with overdraw
        float[] vertices =
        [
            -1f, -1f,  // Bottom-left
             3f, -1f,  // Bottom-right (extends past viewport)
            -1f,  3f   // Top-left (extends past viewport)
        ];

        _quadVao = GL.GenVertexArray();
        _quadVbo = GL.GenBuffer();

        GlStateCache.Current.BindVertexArray(_quadVao);
        GlStateCache.Current.BindBuffer(BufferTarget.ArrayBuffer, _quadVbo);
        GL.BufferData(BufferTarget.ArrayBuffer, vertices.Length * sizeof(float), vertices, BufferUsageHint.StaticDraw);

        // Position attribute at location 0
        GL.VertexAttribPointer(0, 2, VertexAttribPointerType.Float, false, 2 * sizeof(float), 0);
        GL.EnableVertexAttribArray(0);

        GlStateCache.Current.UnbindVertexArray();
        GlStateCache.Current.UnbindBuffer(BufferTarget.ArrayBuffer);

        _quadInitialized = true;
    }

    #endregion

    #region Uniform Helpers

    /// <summary>
    /// Sets a float uniform on the currently bound program.
    /// </summary>
    public static void SetUniform(int location, float value)
    {
        if (location >= 0)
            GL.Uniform1(location, value);
    }

    /// <summary>
    /// Sets an int uniform on the currently bound program.
    /// </summary>
    public static void SetUniform(int location, int value)
    {
        if (location >= 0)
            GL.Uniform1(location, value);
    }

    /// <summary>
    /// Sets a vec2 uniform on the currently bound program.
    /// </summary>
    public static void SetUniform(int location, float x, float y)
    {
        if (location >= 0)
            GL.Uniform2(location, x, y);
    }

    /// <summary>
    /// Sets a vec3 uniform on the currently bound program.
    /// </summary>
    public static void SetUniform(int location, float x, float y, float z)
    {
        if (location >= 0)
            GL.Uniform3(location, x, y, z);
    }

    /// <summary>
    /// Sets a vec4 uniform on the currently bound program.
    /// </summary>
    public static void SetUniform(int location, float x, float y, float z, float w)
    {
        if (location >= 0)
            GL.Uniform4(location, x, y, z, w);
    }

    /// <summary>
    /// Sets a mat4 uniform on the currently bound program.
    /// </summary>
    public static void SetUniformMatrix4(int location, float[] matrix, bool transpose = false)
    {
        if (location >= 0 && matrix.Length == 16)
            GL.UniformMatrix4(location, 1, transpose, matrix);
    }

    #endregion

    #region IDisposable

    /// <summary>
    /// Disposes all managed resources (textures, GBuffers) and the quad VAO/VBO.
    /// </summary>
    public void Dispose()
    {
        if (_isDisposed)
            return;

        // Dispose all tracked resources
        foreach (var resource in _managedResources)
        {
            resource.Dispose();
        }
        _managedResources.Clear();

        // Dispose quad geometry
        if (_quadInitialized)
        {
            GL.DeleteVertexArray(_quadVao);
            GL.DeleteBuffer(_quadVbo);
            _quadVao = 0;
            _quadVbo = 0;
            _quadInitialized = false;
        }

        _isDisposed = true;
    }

    #endregion
}
