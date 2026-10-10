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
        using var bindings = StateCache.Current.BindFramebufferScope();
        using var surface = GpuFramebuffer.Wrap(0, width: 4, height: 4);
        Assert.Throws<NotSupportedException>(() => GraphicsCommandContext.TryRun("UnknownSurface", [], true,
            commands => commands.BeginPass(new(surface, [new(-1, SurfaceBuffer: DrawBuffersEnum.BackLeft)]))));
        surface.PublishSurfaceMetadata(4, 4);
        var metadata = surface.Surface!;
        Assert.Equal(0, surface.ColorAttachmentCount);
        Assert.Empty(surface.AttachmentImages);
        StateCache.Current.BindFramebuffer(FramebufferTarget.Framebuffer, 0);
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
            Assert.True(GraphicsCommandContext.TryRun("PublishedSurface", [], true, commands =>
            {
                commands.BeginPass(new(surface, [new(-1, AttachmentLoad.Clear, Clear: ColorClearValue.Float(1, 0, 0, 1), SurfaceBuffer: metadata.Buffer)]));
                Assert.Throws<InvalidOperationException>(() => surface.PublishSurfaceMetadata(3, 3));
                Assert.Equal(4, commands.PassViewport.Width);
                commands.EndPass();
            }));
            hostile.AssertRestored();
            GL.ReadBuffer((ReadBufferMode)metadata.Buffer);
            using var pack = StateCache.Current.SetPixelPackScope(new(1));
            // The fixture's native window is one pixel; larger published extents below exercise metadata refresh only.
            byte[] pixels = new byte[4]; GL.ReadPixels(0, 0, 1, 1, PixelFormat.Rgba, PixelType.UnsignedByte, pixels);
            Assert.Equal(255, pixels[0]); Assert.Equal(0, pixels[1]); Assert.Equal(0, pixels[2]);
        }
        surface.PublishSurfaceMetadata(3, 2);
        Assert.True(GraphicsCommandContext.TryRun("ResizedSurface", [], true, commands =>
        {
            commands.BeginPass(new(surface, [new(-1, SurfaceBuffer: metadata.Buffer)]));
            Assert.Equal(3, commands.PassViewport.Width); Assert.Equal(2, commands.PassViewport.Height);
            commands.EndPass();
        }));
        var wrong = metadata.Buffer == DrawBuffersEnum.BackLeft ? DrawBuffersEnum.FrontLeft : DrawBuffersEnum.BackLeft;
        Assert.Throws<ArgumentException>(() => GraphicsCommandContext.TryRun("WrongSurfaceRoute", [], true,
            commands => commands.BeginPass(new(surface, [new(-1, SurfaceBuffer: wrong)]))));
        Assert.Equal(ErrorCode.NoError, GL.GetError());
    }

    /// <summary>A prepared graphics pipeline draws into the published native window format under hostile state.</summary>
    [Fact]
    public void PreparedPipelineDrawsToPublishedSurface()
    {
        EnsureContextValid();
        using var bindings = StateCache.Current.BindFramebufferScope();
        using var surface = GpuFramebuffer.Wrap(0, width: 1, height: 1);
        surface.PublishSurfaceMetadata(1, 1);
        var metadata = surface.Surface!;
        using var programs = new ComponentShaderPrograms();
        var shader = programs.Create<DepthHierarchyDownsampleShaderProgram>();
        using var lifetime = new GraphicsPipelineLifetime();
        var layout = new VertexLayoutDesc([new(0, 3, VertexAttribPointerType.Float, VertexInterpretation.Floating, 0, 0, 12)]);
        using var pipeline = new GraphicsPipeline(lifetime, new(shader.GraphicsIdentity!, layout,
            new([new(metadata.Color)], metadata.DepthStencil, metadata.Samples, metadata.HasDepth, metadata.HasStencil), DynamicPipelineState.Viewport), shader);
        using var buffer = GpuVbo.Create();
        buffer.UploadData(new float[] { -1, -1, 0, 3, -1, 0, -1, 3, 0 });
        using var geometry = new ArrayGraphicsGeometry(layout, PrimitiveType.Triangles, new Dictionary<int, GpuVbo> { [0] = buffer });
        using var source = DynamicTexture2D.CreateWithData(2, 2, PixelInternalFormat.R32f, [.25f, .5f, .75f, 1]);
        shader.HzbDepth = source; shader.SrcMip = 0;
        using var hostile = new HostileFullscreenState();
        Assert.True(GraphicsCommandContext.TryRun("DrawSurface", [pipeline], true, commands =>
        {
            commands.BeginPass(new(surface, [new(-1, SurfaceBuffer: metadata.Buffer)]));
            commands.SetPipeline(pipeline); commands.SetDynamicState(new() { Viewport = commands.PassViewport });
            commands.Draw(geometry, new(0, 3)); commands.EndPass();
        }));
        hostile.AssertRestored();
        StateCache.Current.BindFramebuffer(FramebufferTarget.ReadFramebuffer, 0); GL.ReadBuffer((ReadBufferMode)metadata.Buffer);
        using var pack = StateCache.Current.SetPixelPackScope(new(1));
        byte[] pixels = new byte[4]; GL.ReadPixels(0, 0, 1, 1, PixelFormat.Rgba, PixelType.UnsignedByte, pixels);
        Assert.InRange(pixels[0], (byte)63, (byte)65);
        Assert.Equal(ErrorCode.NoError, GL.GetError());
    }
    #endregion
}
