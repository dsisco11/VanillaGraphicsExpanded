using VanillaGraphicsExpanded.Rendering.Integration.OpenGL;
using VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;
using VanillaGraphicsExpanded.Rendering.Pipeline.Descriptions;
using VanillaGraphicsExpanded.Tests.GPU.Fixtures;
using VanillaGraphicsExpanded.Rendering.RHI;
using System;
using System.Collections.Generic;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Rendering.Shaders;

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
/// using var depthTex = framework.CreateTexture(4, 4, TextureFormat.R32f, depthData);
/// using var normalTex = framework.CreateTexture(4, 4, TextureFormat.Rgba16f, normalData);
///
/// // Create output render target
/// using var outputGBuffer = framework.CreateTestGBuffer(2, 2, TextureFormat.Rgba16f, 2);
///
/// // Bind inputs and render
/// depthTex.Bind(0);
/// normalTex.Bind(1);
/// FramebufferTestSetup.BindForEngineRendering(outputGBuffer);
/// framework.RenderQuad(programId);
///
/// // Read back and validate
/// var result = outputGBuffer[0].ReadPixels();
/// </code>
/// </remarks>
public sealed class ShaderTestFramework : IDisposable
{
    #region Fields

    private GpuVao? _quadVao;
    private GpuVbo? _quadVbo;
    private GpuEbo? _quadEbo;
    private static readonly RHIVertexLayout QuadLayout = new([new(0,3,VertexComponentFormat.Float,VertexInterpretation.Floating,0,0,20),new(1,2,VertexComponentFormat.Float,VertexInterpretation.Floating,0,12,20)]);
    private bool _quadInitialized;
    private bool _isDisposed;
    private readonly List<IDisposable> _managedResources = [];
    private static readonly GlPipelineDesc FullscreenPassPso = CreateFullscreenPassPso();

    #endregion

    /// <summary>Defines deterministic fixed-function state for fullscreen component passes.</summary>
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
        TextureFormat format,
        float[] data,
        TextureFilterMode filter = TextureFilterMode.Nearest)
    {
        var texture = RHICommandContext.Current.CreateDynamicTexture2D(new RHITextureDescription { Dimension = TextureDimension.Two, Width = width, Height = height, Format = format, MipLevels = 1, Storage = ResourceStorageMode.Mutable, BaseLevel = 0, MaxLevel = 0, Filter = filter, Label = null }, [new RHITextureInitialData(new(new(), 0, 0, 0, width, height), new RHIPixelEncoding(TextureFormatHelper.GetPixelFormat(format), PixelComponentType.Float), System.Runtime.InteropServices.MemoryMarshal.AsBytes((data).AsSpan()).ToArray())]).RequireResource();
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
        TextureFormat format,
        TextureFilterMode filter = TextureFilterMode.Nearest)
    {
        var texture = RHICommandContext.Current.CreateDynamicTexture2D(new RHITextureDescription { Dimension = TextureDimension.Two, Format = format, Width = System.Math.Max(1, width), Height = System.Math.Max(1, height), MipLevels = 1, Storage = ResourceStorageMode.Mutable, Filter = filter, BaseLevel = 0, MaxLevel = (1) - 1, Label = null }).RequireResource();
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
        TextureFormat format,
        int attachmentCount = 1)
    {
        var textures = new DynamicTexture2D[attachmentCount];
        for (int i = 0; i < attachmentCount; i++)
        {
            textures[i] = RHICommandContext.Current.CreateDynamicTexture2D(new RHITextureDescription { Dimension = TextureDimension.Two, Format = format, Width = System.Math.Max(1, width), Height = System.Math.Max(1, height), MipLevels = 1, Storage = ResourceStorageMode.Mutable, Filter = TextureFilterMode.Nearest, BaseLevel = 0, MaxLevel = (1) - 1, Label = null }).RequireResource();
            _managedResources.Add(textures[i]);
        }

        var gBuffer = DynamicGpuFramebuffer.CreateMRT(textures, depthTexture: null);
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
        params TextureFormat[] formats)
    {
        var textures = new DynamicTexture2D[formats.Length];
        for (int i = 0; i < formats.Length; i++)
        {
            textures[i] = RHICommandContext.Current.CreateDynamicTexture2D(new RHITextureDescription { Dimension = TextureDimension.Two, Format = formats[i], Width = System.Math.Max(1, width), Height = System.Math.Max(1, height), MipLevels = 1, Storage = ResourceStorageMode.Mutable, Filter = TextureFilterMode.Nearest, BaseLevel = 0, MaxLevel = (1) - 1, Label = null }).RequireResource();
            _managedResources.Add(textures[i]);
        }

        var gBuffer = DynamicGpuFramebuffer.CreateMRT(textures, depthTexture: null);
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
        OpenGlStateCache.Current.Apply(FullscreenPassPso);
        OpenGLProgramSelection.Select(OpenGlStateCache.Current, programId);
        RenderGeometry();
        OpenGLProgramSelection.Select(OpenGlStateCache.Current, 0);
    }

    /// <summary>Issues an explicitly native engine fixture draw while the engine caller owns executable and target state.</summary>
    public void RenderGeometry()
    {
        EnsureQuadInitialized();
        using var geometry = OpenGlStateCache.Current.BindVertexArrayScope(_quadVao!.VertexArrayId);
        OpenGLVertexSelection.Bind(OpenGlStateCache.Current, _quadVao!.VertexArrayId);
        OpenTK.Graphics.OpenGL4.GL.DrawElements(OpenTK.Graphics.OpenGL4.PrimitiveType.Triangles, 3, OpenTK.Graphics.OpenGL4.DrawElementsType.UnsignedInt, 0);
    }

    /// <summary>Imports the fixture's currently selected engine target explicitly, then submits production bindings through the RHI.</summary>
    public void RenderQuad(GpuProgram program)
    {
        int name=OpenGlStateCache.Current.GetCurrentFramebuffer(FramebufferTarget.DrawFramebuffer);
        int[] viewport=new int[4]; GL.GetInteger(GetPName.Viewport,viewport);
        using var target=DynamicGpuFramebuffer.Wrap(name,"Fixture.EngineDestination",viewport[2],viewport[3]);
        target.PublishRenderPassMetadata();
        SubmitProductionQuad(program,target,null,false);
    }

    /// <summary>Creates the actual fixed pipeline and submits captured inputs to a fixture-owned attachment pass.</summary>
    public void RenderQuadTo(GpuProgram program, GpuFramebuffer target, (float r,float g,float b,float a)? clearColor=null, Action? afterDraw=null)
        => SubmitProductionQuad(program,target,clearColor,true,afterDraw);

    /// <summary>Freezes explicit fixture load policy and executes the exact captured shader generation.</summary>
    private void SubmitProductionQuad(GpuProgram program,GpuFramebuffer target,(float r,float g,float b,float a)? clearColor,bool clear,Action? afterDraw=null)
    {
        EnsureQuadInitialized();
        var color=clearColor??(0f,0f,0f,0f);
        var pass=FramebufferTestSetup.DescribeAttachments(target,Enumerable.Range(0,target.ColorAttachmentCount).Select(index=>
            new RHIColorAttachment(AttachmentIndex:index,Load:clear?AttachmentLoad.Clear:AttachmentLoad.Preserve,
                Clear:clear?ColorClearValue.Float(color.Item1,color.Item2,color.Item3,color.Item4):null)));
        RenderQuadTo(program,pass,afterDraw);
    }

    /// <summary>Submits fixed shader inputs directly to supplied final attachment intentions.</summary>
    public void RenderQuadTo(GpuProgram program,RHIRenderPassDescription pass,Action? afterDraw=null)
    {
        EnsureQuadInitialized();
        var commands=RHICommandContext.Current;
        using var pipeline=commands.CreateGraphicsPipeline(new(program.Shader,QuadLayout,commands.DescribeRenderTargets(pass),DynamicPipelineState.Viewport)).RequireResource();
        var geometry=new RHIGeometryInputs(QuadLayout,[new(0,new(_quadVbo!,0,_quadVbo!.SizeBytes),20)],new(new(_quadEbo!,0,_quadEbo!.SizeBytes),IndexFormat.UnsignedInt));
        if (!commands.TryEnterBoundary(new("Fixture.Fullscreen",true,graphics:[pipeline],operations:BoundaryOperations.RenderPasses),out var admitted))
            throw new InvalidOperationException($"Fixture boundary admission failed: {OpenGlStateCache.Current.BoundaryEntryFailure}");
        using var boundary=admitted!;
        boundary.Run(()=>
        {
            commands.BeginRenderPass(pass);commands.SetPipeline(pipeline);
            commands.SetDynamicState(new(){Viewport=commands.PassViewport});commands.BindResources(program.CaptureBindings());
            commands.DrawIndexed(geometry,new(0,3));
            // Binding observations must run before boundary restoration withdraws the submitted table.
            afterDraw?.Invoke();commands.EndRenderPass();
        });
    }

    /// <summary>Draws explicitly native GLSL fixtures to their engine-owned target without wrapping them as portable shaders.</summary>
    public void RenderQuadTo(int programId,GpuFramebuffer target,(float r,float g,float b,float a)? clearColor=null)
    {
        var color=clearColor??(0f,0f,0f,0f);
        FramebufferTestSetup.ClearAttachments(target,color.Item1,color.Item2,color.Item3,color.Item4);
        using var binding=OpenGlStateCache.Current.BindFramebufferScope();
        FramebufferTestSetup.BindForEngineRendering(target);RenderQuad(programId);
    }

    /// <summary>Configures native fixture geometry using core named vertex-array commands, independent of production realization caches.</summary>
    private void EnsureQuadInitialized()
    {
        if(_quadInitialized)return;
        _quadVao=NativeResourceSetup.CreateVertexArray("Tests.FullscreenTriangle");
        _quadVbo=RHICommandContext.Current.CreateVertexBuffer(new(){Role=BufferRole.Vertex,Mode=ResourceStorageMode.Mutable,Capacity=60,Usage=BufferUsage.StaticDraw}).RequireResource();
        _quadEbo=RHICommandContext.Current.CreateIndexBuffer(new(){Role=BufferRole.Index,Mode=ResourceStorageMode.Mutable,Capacity=12,Usage=BufferUsage.StaticDraw}).RequireResource();
        _quadVbo.UploadData(new float[]{-1,-1,0,0,0,3,-1,0,2,0,-1,3,0,0,2});
        _quadEbo.UploadIndices(new uint[]{0,1,2});
        OpenTK.Graphics.OpenGL4.GL.VertexArrayVertexBuffer(_quadVao.VertexArrayId,0,_quadVbo.BufferId,IntPtr.Zero,20);
        for(int attribute=0;attribute<2;attribute++)
        {
            OpenTK.Graphics.OpenGL4.GL.EnableVertexArrayAttrib(_quadVao.VertexArrayId,attribute);
            OpenTK.Graphics.OpenGL4.GL.VertexArrayAttribFormat(_quadVao.VertexArrayId,attribute,attribute==0?3:2,OpenTK.Graphics.OpenGL4.VertexAttribType.Float,false,attribute==0?0:12);
            OpenTK.Graphics.OpenGL4.GL.VertexArrayAttribBinding(_quadVao.VertexArrayId,attribute,0);
        }
        OpenTK.Graphics.OpenGL4.GL.VertexArrayElementBuffer(_quadVao.VertexArrayId,_quadEbo.BufferId);
        _quadInitialized=true;
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
            _quadEbo?.Dispose();
            _quadVao?.Dispose();
            _quadVbo?.Dispose();
            _quadEbo = null;
            _quadVao = null;
            _quadVbo = null;
            _quadInitialized = false;
        }

        _isDisposed = true;
    }

    #endregion
}
