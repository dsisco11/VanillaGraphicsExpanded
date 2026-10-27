using VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;
using System;
using VanillaGraphicsExpanded.Rendering.RHI;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Rendering.Pipeline;
using VanillaGraphicsExpanded.Rendering.Pipeline.Passes;
using VanillaGraphicsExpanded.Rendering.Pipeline.Descriptions;
using VanillaGraphicsExpanded.LumOn;
using VanillaGraphicsExpanded.Tests.GPU.Fixtures;

namespace VanillaGraphicsExpanded.Tests.GPU;

/// <summary>Exercises the actual window surface without manufacturing image attachments or format metadata.</summary>
[Collection("GPU")]
[Trait("Category", "GPU")]
public sealed class SurfaceRenderPassTests(HeadlessGLFixture fixture) : RenderTestBase(fixture)
{
    #region Public API
    /// <summary>Published front/back routing clears real pixels, restores hostile state, and guards active metadata changes.</summary>
    [Fact]
    public void PublishedSurfaceClearsAndRefreshesWithoutFakeAttachments()
    {
        EnsureContextValid();
        using var bindings = OpenGlStateCache.Current.BindFramebufferScope();
        using var surface = DynamicGpuFramebuffer.Wrap(0, width: 4, height: 4);
        Assert.Throws<NotSupportedException>(() => RHICommandContext.Current.DescribeRenderTargets(FramebufferTestSetup.DescribeAttachments(surface, [new(WindowBuffer: SurfaceColorBuffer.BackLeft)])));
        surface.PublishSurfaceMetadata(4, 4);
        var metadata = surface.Surface!;
        Assert.Equal(0, surface.ColorAttachmentCount);
        Assert.Empty(surface.AttachmentImages);
        OpenGLFramebufferSelection.Bind(OpenGlStateCache.Current, FramebufferTarget.Framebuffer, 0);
        Assert.Equal(GL.GetInteger(GetPName.Doublebuffer) != 0 ? DrawBuffersEnum.BackLeft : DrawBuffersEnum.FrontLeft, metadata.Buffer);
        Assert.Equal(Math.Max(1, GL.GetInteger(GetPName.Samples)), metadata.Samples);
        var attachment = metadata.Buffer == DrawBuffersEnum.BackLeft ? FramebufferAttachment.BackLeft : FramebufferAttachment.FrontLeft;
        GL.GetFramebufferAttachmentParameter(FramebufferTarget.Framebuffer, attachment, FramebufferParameterName.FramebufferAttachmentRedSize, out int red);
        GL.GetFramebufferAttachmentParameter(FramebufferTarget.Framebuffer, attachment, FramebufferParameterName.FramebufferAttachmentAlphaSize, out int alpha);
        GL.GetFramebufferAttachmentParameter(FramebufferTarget.Framebuffer, attachment, FramebufferParameterName.FramebufferAttachmentColorEncoding, out int encoding);
        Assert.Equal(metadata.Color == PixelInternalFormat.Rgb10A2 ? 10 : 8, red);
        Assert.Equal(metadata.Color is PixelInternalFormat.Rgb8 or PixelInternalFormat.Srgb8 ? 0 : metadata.Color == PixelInternalFormat.Rgb10A2 ? 2 : 8, alpha);
        Assert.Equal(metadata.Color is PixelInternalFormat.Srgb8 or PixelInternalFormat.Srgb8Alpha8, encoding == (int)All.Srgb);
        GL.GetFramebufferAttachmentParameter(FramebufferTarget.Framebuffer, FramebufferAttachment.Depth, FramebufferParameterName.FramebufferAttachmentDepthSize, out int depthBits);
        GL.GetFramebufferAttachmentParameter(FramebufferTarget.Framebuffer, FramebufferAttachment.Stencil, FramebufferParameterName.FramebufferAttachmentStencilSize, out int stencilBits);
        Assert.Equal(depthBits > 0, metadata.HasDepth);
        Assert.Equal(stencilBits > 0, metadata.HasStencil);
        using (var hostile = new HostileFullscreenState())
        {
            {
    var commands=RHICommandContext.Current;
    using var scope80=commands.EnterBoundary(new("PublishedSurface",true,graphics:System.Linq.Enumerable.Select<RHIGraphicsPipeline,RHIGraphicsPipelineRef>([],value=>value),operations:BoundaryOperations.RenderPasses));
    scope80.Run(()=>{
                commands.BeginRenderPass(FramebufferTestSetup.DescribeAttachments(surface, [new(Load:AttachmentLoad.Clear, Clear: ColorClearValue.Float(1, 0, 0, 1), WindowBuffer: metadata.Buffer == DrawBuffersEnum.BackLeft ? SurfaceColorBuffer.BackLeft : SurfaceColorBuffer.FrontLeft)]));
                Assert.Throws<InvalidOperationException>(() => surface.PublishSurfaceMetadata(3, 3));
                Assert.Equal(4, commands.PassViewport.Width);
                commands.EndRenderPass();
            });
}
            hostile.AssertRestored();
            GL.ReadBuffer((ReadBufferMode)metadata.Buffer);
            using var pack = OpenGLPixelLayouts.PackScope(OpenGlStateCache.Current, new(1));
            // The fixture's native window is one pixel; larger published extents below exercise metadata refresh only.
            byte[] pixels = new byte[4]; GL.ReadPixels(0, 0, 1, 1, PixelFormat.Rgba, PixelType.UnsignedByte, pixels);
            Assert.Equal(255, pixels[0]); Assert.Equal(0, pixels[1]); Assert.Equal(0, pixels[2]);
        }
        surface.PublishSurfaceMetadata(3, 2);
        {
    var commands=RHICommandContext.Current;
    using var scope81=commands.EnterBoundary(new("ResizedSurface",true,graphics:System.Linq.Enumerable.Select<RHIGraphicsPipeline,RHIGraphicsPipelineRef>([],value=>value),operations:BoundaryOperations.RenderPasses));
    scope81.Run(()=>{
            commands.BeginRenderPass(FramebufferTestSetup.DescribeAttachments(surface, [new(WindowBuffer: metadata.Buffer == DrawBuffersEnum.BackLeft ? SurfaceColorBuffer.BackLeft : SurfaceColorBuffer.FrontLeft)]));
            Assert.Equal(3, commands.PassViewport.Width); Assert.Equal(2, commands.PassViewport.Height);
            commands.EndRenderPass();
        });
}
        var wrong = metadata.Buffer == DrawBuffersEnum.BackLeft ? DrawBuffersEnum.FrontLeft : DrawBuffersEnum.BackLeft;
        Assert.Throws<ArgumentException>(() => RHICommandContext.Current.DescribeRenderTargets(FramebufferTestSetup.DescribeAttachments(surface, [new(WindowBuffer: wrong == DrawBuffersEnum.BackLeft ? SurfaceColorBuffer.BackLeft : SurfaceColorBuffer.FrontLeft)])));
        Assert.Equal(ErrorCode.NoError, GL.GetError());
    }

    /// <summary>A prepared graphics pipeline draws into the published native window format under hostile state.</summary>
    [Fact]
    public void PreparedPipelineDrawsToPublishedSurface()
    {
        EnsureContextValid();
        using var bindings = OpenGlStateCache.Current.BindFramebufferScope();
        using var surface = DynamicGpuFramebuffer.Wrap(0, width: 1, height: 1);
        surface.PublishSurfaceMetadata(1, 1);
        var metadata = surface.Surface!;
        using var programs = new ComponentShaderPrograms();
        var shader = programs.Create<RasterDepthReductionShaderProgram>();
        var layout = new RHIVertexLayout([new(0, 3, VertexComponentFormat.Float, VertexInterpretation.Floating, 0, 0, 12)]);
        using var pipeline = RHICommandContext.Current.CreateGraphicsPipeline(new(shader.Shader, layout,
            new([new(OpenGLImageValues.Format(metadata.Color))], metadata.DepthStencil is { } format ? OpenGLImageValues.Format(format) : null, metadata.Samples, metadata.HasDepth, metadata.HasStencil), DynamicPipelineState.Viewport)).RequireResource();
        using var buffer = RHICommandContext.Current.CreateVertexBuffer(new RHIBufferDescription { Role = BufferRole.Vertex, Mode = ResourceStorageMode.Mutable, Capacity = 0, Usage = BufferUsage.StaticDraw, Label = null }).RequireResource();
        buffer.UploadData(new float[] { -1, -1, 0, 3, -1, 0, -1, 3, 0 });
        using var geometry = new ArrayGraphicsGeometry(layout, PrimitiveTopology.Triangles, new Dictionary<int, GpuVbo> { [0] = buffer });
        using var source = RHICommandContext.Current.CreateDynamicTexture2D(new RHITextureDescription { Dimension = TextureDimension.Two, Width = 2, Height = 2, Format = TextureFormat.R32f, MipLevels = 1, Storage = ResourceStorageMode.Mutable, BaseLevel = 0, MaxLevel = 0, Filter = TextureFilterMode.Nearest, Label = null }, [new RHITextureInitialData(new(new(), 0, 0, 0, 2, 2), VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL.OpenGLPixelValues.Encoding(TextureFormatHelper.GetPixelFormat(PixelInternalFormat.R32f), OpenTK.Graphics.OpenGL.PixelType.Float), System.Runtime.InteropServices.MemoryMarshal.AsBytes((new float[] { .25f, .5f, .75f, 1 }).AsSpan()).ToArray())]).RequireResource();
        shader.HzbDepth = source; shader.SrcMip = 0;
        using var hostile = new HostileFullscreenState();
        {
    var commands=RHICommandContext.Current;
    using var scope82=commands.EnterBoundary(new("DrawSurface",true,graphics:System.Linq.Enumerable.Select<RHIGraphicsPipeline,RHIGraphicsPipelineRef>([pipeline],value=>value),operations:BoundaryOperations.RenderPasses));
    scope82.Run(()=>{
            commands.BeginRenderPass(FramebufferTestSetup.DescribeAttachments(surface, [new(WindowBuffer: metadata.Buffer == DrawBuffersEnum.BackLeft ? SurfaceColorBuffer.BackLeft : SurfaceColorBuffer.FrontLeft)]));
            commands.SetPipeline(pipeline); commands.SetDynamicState(new() { Viewport = commands.PassViewport });
            commands.BindResources(shader.CaptureBindings()); commands.Draw(geometry.Inputs, new(0, 3)); commands.EndRenderPass();
        });
}
        hostile.AssertRestored();
        OpenGLFramebufferSelection.Bind(OpenGlStateCache.Current, FramebufferTarget.ReadFramebuffer, 0); GL.ReadBuffer((ReadBufferMode)metadata.Buffer);
        using var pack = OpenGLPixelLayouts.PackScope(OpenGlStateCache.Current, new(1));
        byte[] pixels = new byte[4]; GL.ReadPixels(0, 0, 1, 1, PixelFormat.Rgba, PixelType.UnsignedByte, pixels);
        Assert.InRange(pixels[0], (byte)63, (byte)65);
        Assert.Equal(ErrorCode.NoError, GL.GetError());
    }
    #endregion
}
