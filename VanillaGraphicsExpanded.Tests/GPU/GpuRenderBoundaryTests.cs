using VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;
using VanillaGraphicsExpanded.Rendering.RHI;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Tests.GPU.Fixtures;
using Vintagestory.Client.NoObf;

namespace VanillaGraphicsExpanded.Tests.GPU;

/// <summary>Exercises framebuffer and shader ownership across failed isolated rendering work.</summary>
[Collection("GPU")]
[Trait("Category", "GPU")]
public sealed class GpuRenderBoundaryTests(HeadlessGLFixture fixture) : RenderTestBase(fixture)
{
    #region Public API
    /// <summary>Restores actual independent framebuffer bindings after an exception without changing routing.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void FramebufferScopeRestoresActualEngineBindings(bool defaultFramebuffer)
    {
        EnsureContextValid();
        using var draw = CreateMRTRenderTarget(2, 2, PixelInternalFormat.Rgba32f, PixelInternalFormat.Rgba32f, PixelInternalFormat.Rgba32f);
        using var read = CreateRenderTarget(1, 1, PixelInternalFormat.Rgba32f);
        using var nested = CreateRenderTarget(3, 3, PixelInternalFormat.Rgba32f);
        // Raw writes outside supported interception explicitly withdraw primed binding knowledge.
        OpenGLFramebufferSelection.Bind(OpenGlStateCache.Current, FramebufferTarget.Framebuffer, nested.FboId);
        GL.BindFramebuffer(FramebufferTarget.DrawFramebuffer, defaultFramebuffer ? 0 : draw.FboId);
        GL.BindFramebuffer(FramebufferTarget.ReadFramebuffer, read.FboId);
        OpenGlStateCache.Current.Invalidate(EPipelineState.Bindings);
        GL.ReadBuffer(ReadBufferMode.None);
        DrawBuffersEnum[] routing = [DrawBuffersEnum.ColorAttachment2, DrawBuffersEnum.None, DrawBuffersEnum.ColorAttachment0];
        if (defaultFramebuffer) { GL.DrawBuffer(DrawBufferMode.Back); OpenGlStateCache.Current.Invalidate(EPipelineState.FramebufferRouting); }
        else { GL.DrawBuffers(routing.Length, routing); OpenGlStateCache.Current.Invalidate(EPipelineState.FramebufferRouting); }
        GL.Viewport(1, 2, 7, 9);
        Assert.Throws<InvalidOperationException>((Action)(() =>
        {
            using var scope = OpenGlStateCache.Current.BindFramebufferScope();
            nested.Bind();
            throw new InvalidOperationException("Controlled render failure.");
        }));
        Assert.Equal(defaultFramebuffer ? 0 : draw.FboId, GL.GetInteger(GetPName.DrawFramebufferBinding));
        Assert.Equal(read.FboId, GL.GetInteger(GetPName.ReadFramebufferBinding));
        Assert.Equal((int)ReadBufferMode.None, GL.GetInteger(GetPName.ReadBuffer));
        Assert.Equal((int)(defaultFramebuffer ? DrawBufferMode.Back : DrawBufferMode.ColorAttachment2), GL.GetInteger(GetPName.DrawBuffer0));
        if (!defaultFramebuffer)
            for (int i = 0; i < routing.Length; i++)
                Assert.Equal((int)routing[i], GL.GetInteger((GetPName)((int)GetPName.DrawBuffer0 + i)));
        Assert.Equal(defaultFramebuffer ? 0 : draw.FboId, OpenGlStateCache.Current.GetCurrentFramebuffer(FramebufferTarget.DrawFramebuffer));
        Assert.Equal(read.FboId, OpenGlStateCache.Current.GetCurrentFramebuffer(FramebufferTarget.ReadFramebuffer));
        int[] viewport = new int[4];
        GL.GetInteger(GetPName.Viewport, viewport);
        Assert.Equal(new[] { 1, 2, 7, 9 }, viewport);
        Assert.Equal(ErrorCode.NoError, GL.GetError());
        GpuFramebuffer.Unbind();
    }

    /// <summary>Retains established MRT routing and clears independently authored floating-point attachments.</summary>
    [Fact]
    public void ExistingBindAndClearMethodsClearAllFloatOutputs()
    {
        EnsureContextValid();
        using var state = LegacyFixedFunctionReference.Capture(OpenGlStateCache.Current);
        using var target = CreateMRTRenderTarget(2, 2, PixelInternalFormat.Rgba32f, PixelInternalFormat.R32f, PixelInternalFormat.R32f);
        target[0].UploadDataImmediate(Enumerable.Repeat(2f, 16).ToArray());
        target[1].UploadDataImmediate(Enumerable.Repeat(3f, 4).ToArray());
        target[2].UploadDataImmediate(Enumerable.Repeat(17f, 4).ToArray());
        GL.Disable(EnableCap.ScissorTest);
        GL.ColorMask(true, true, true, true);
        FramebufferTestSetup.BindForEngineRendering(target);
        FramebufferTestSetup.ClearAttachments(target, 0, 0, 0, 0);
        Assert.Equal((int)DrawBuffersEnum.ColorAttachment0, GL.GetInteger(GetPName.DrawBuffer0));
        Assert.Equal((int)DrawBuffersEnum.ColorAttachment1, GL.GetInteger(GetPName.DrawBuffer1));
        Assert.Equal((int)DrawBuffersEnum.ColorAttachment2, GL.GetInteger(GetPName.DrawBuffer2));
        Assert.All(target[0].ReadPixels(), value => Assert.Equal(0f, value));
        Assert.All(target[1].ReadPixels(), value => Assert.Equal(0f, value));
        Assert.All(target[2].ReadPixels(), value => Assert.Equal(0f, value));
        Assert.Equal(ErrorCode.NoError, GL.GetError());
    }

    /// <summary>Borrowed framebuffers use explicit viewport metadata without taking resource ownership.</summary>
    [Fact]
    public void WrappedFramebufferUsesViewportWithoutOwningResources()
    {
        EnsureContextValid();
        using var scope = OpenGlStateCache.Current.BindFramebufferScope();
        using var target = CreateRenderTarget(3, 5, PixelInternalFormat.Rgba32f);
        using (var borrowed = DynamicGpuFramebuffer.Wrap(target.FboId, "Borrowed test target", width: 3, height: 5))
        {
            Assert.Equal(3, borrowed.Width);
            Assert.Equal(5, borrowed.Height);
            FramebufferTestSetup.BindForEngineRendering(borrowed);
            int[] viewport = new int[4];
            GL.GetInteger(GetPName.Viewport, viewport);
            Assert.Equal(new[] { 0, 0, 3, 5 }, viewport);
        }
        Assert.True(GL.IsFramebuffer(target.FboId));
        Assert.True(GL.IsTexture(target[0].TextureId));
        Assert.Equal(ErrorCode.NoError, GL.GetError());
    }

    /// <summary>Scratch blits copy attachment zero while leaving target routing and caller state untouched.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void BlitPreservesTargetAndCallerRouting(bool wrapped)
    {
        EnsureContextValid();
        using var scope = OpenGlStateCache.Current.BindFramebufferScope();
        using var source = CreateMRTRenderTarget(2, 2, PixelInternalFormat.Rgba32f, PixelInternalFormat.Rgba32f);
        using var destination = CreateMRTRenderTarget(2, 2, PixelInternalFormat.Rgba32f, PixelInternalFormat.Rgba32f);
        using var callerDraw = CreateMRTRenderTarget(2, 2, PixelInternalFormat.Rgba32f, PixelInternalFormat.Rgba32f);
        using var callerRead = CreateRenderTarget(2, 2, PixelInternalFormat.Rgba32f);
        source[0].UploadDataImmediate(Enumerable.Repeat(7f, 16).ToArray());
        source[1].UploadDataImmediate(Enumerable.Repeat(11f, 16).ToArray());
        destination[0].UploadDataImmediate(new float[16]);
        destination[1].UploadDataImmediate(Enumerable.Repeat(13f, 16).ToArray());
        DrawBuffersEnum[] routing = [DrawBuffersEnum.None, DrawBuffersEnum.ColorAttachment1];
        foreach (var target in new[] { source, destination, callerDraw })
        {
            target.Bind();
            GL.DrawBuffers(routing.Length, routing); OpenGlStateCache.Current.Invalidate(EPipelineState.FramebufferRouting);
            GL.ReadBuffer(ReadBufferMode.ColorAttachment1);
        }
        GL.BindFramebuffer(FramebufferTarget.DrawFramebuffer, callerDraw.FboId);
        GL.BindFramebuffer(FramebufferTarget.ReadFramebuffer, callerRead.FboId);
        GL.ReadBuffer(ReadBufferMode.None);
        GL.Viewport(1, 2, 3, 4);
        // Raw test setup bypasses interception; scopes must recover these external selections.
        OpenGlStateCache.Current.Invalidate(EPipelineState.FramebufferBindings);
        using var wrappedSource = DynamicGpuFramebuffer.Wrap(source.FboId, width: 2, height: 2);
        using var wrappedDestination = DynamicGpuFramebuffer.Wrap(destination.FboId, width: 2, height: 2);
        var copy = FramebufferTestSetup.DescribeCopy(source, destination);
        RHICommandContext.Current.BlitImages(copy);
        Assert.Equal(callerDraw.FboId, GL.GetInteger(GetPName.DrawFramebufferBinding));
        Assert.Equal(callerRead.FboId, GL.GetInteger(GetPName.ReadFramebufferBinding));
        Assert.Equal((int)ReadBufferMode.None, GL.GetInteger(GetPName.ReadBuffer));
        int[] viewport = new int[4];
        GL.GetInteger(GetPName.Viewport, viewport);
        Assert.Equal(new[] { 1, 2, 3, 4 }, viewport);
        Assert.All(destination[0].ReadPixels(), value => Assert.Equal(7f, value));
        Assert.All(destination[1].ReadPixels(), value => Assert.Equal(13f, value));
        foreach (var target in new[] { source, destination, callerDraw })
        {
            target.Bind();
            Assert.Equal((int)routing[0], GL.GetInteger(GetPName.DrawBuffer0));
            Assert.Equal((int)routing[1], GL.GetInteger(GetPName.DrawBuffer1));
            Assert.Equal((int)ReadBufferMode.ColorAttachment1, GL.GetInteger(GetPName.ReadBuffer));
        }
        Assert.Equal(ErrorCode.NoError, GL.GetError());
    }

    /// <summary>Reusable scratch attachments follow replacement textures without taking their ownership.</summary>
    [Fact]
    public void BlitFollowsReplacedBorrowedAttachments()
    {
        EnsureContextValid();
        using var scope = OpenGlStateCache.Current.BindFramebufferScope();
        using var first = RHICommandContext.Current.CreateDynamicTexture2D(new RHITextureDescription { Dimension = TextureDimension.Two, Format = TextureFormat.Rgba32f, Width = System.Math.Max(1, 2), Height = System.Math.Max(1, 2), MipLevels = 1, Storage = ResourceStorageMode.Mutable, Filter = TextureFilterMode.Nearest, BaseLevel = 0, MaxLevel = (1) - 1, Label = null }).RequireResource();
        using var replacement = RHICommandContext.Current.CreateDynamicTexture2D(new RHITextureDescription { Dimension = TextureDimension.Two, Format = TextureFormat.Rgba32f, Width = System.Math.Max(1, 2), Height = System.Math.Max(1, 2), MipLevels = 1, Storage = ResourceStorageMode.Mutable, Filter = TextureFilterMode.Nearest, BaseLevel = 0, MaxLevel = (1) - 1, Label = null }).RequireResource();
        using var output = RHICommandContext.Current.CreateDynamicTexture2D(new RHITextureDescription { Dimension = TextureDimension.Two, Format = TextureFormat.Rgba32f, Width = System.Math.Max(1, 2), Height = System.Math.Max(1, 2), MipLevels = 1, Storage = ResourceStorageMode.Mutable, Filter = TextureFilterMode.Nearest, BaseLevel = 0, MaxLevel = (1) - 1, Label = null }).RequireResource();
        first.UploadDataImmediate(Enumerable.Repeat(3f, 16).ToArray());
        replacement.UploadDataImmediate(Enumerable.Repeat(9f, 16).ToArray());
        using (var source = DynamicGpuFramebuffer.CreateSingle(first)!)
        using (var destination = DynamicGpuFramebuffer.CreateSingle(output)!)
        {
            var copy = FramebufferTestSetup.DescribeCopy(source, destination);
            RHICommandContext.Current.BlitImages(copy);
            Assert.All(output.ReadPixels(), value => Assert.Equal(3f, value));
            source.Attach(replacement);
            copy=FramebufferTestSetup.DescribeCopy(source, destination);
            RHICommandContext.Current.BlitImages(copy);
            Assert.All(output.ReadPixels(), value => Assert.Equal(9f, value));
        }
        Assert.True(GL.IsTexture(first.TextureId));
        Assert.True(GL.IsTexture(replacement.TextureId));
        Assert.True(GL.IsTexture(output.TextureId));
        Assert.Equal(ErrorCode.NoError, GL.GetError());
    }

    /// <summary>Repeated copies reuse setup, and releasing the blitter leaves borrowed framebuffers and textures alive.</summary>
    [Fact]
    public void BlitterRepeatedCopiesAndDisposalPreserveBorrowedTargets()
    {
        EnsureContextValid();
        using var scope = OpenGlStateCache.Current.BindFramebufferScope();
        using var source = CreateRenderTarget(2, 2, PixelInternalFormat.Rgba32f);
        using var destination = CreateRenderTarget(2, 2, PixelInternalFormat.Rgba32f);
        var copy = FramebufferTestSetup.DescribeCopy(source, destination);
        {
            // Repeated blits must continue to copy the current source data.
            foreach (float value in new[] { 3f, 7f, 11f })
            {
                source[0].UploadDataImmediate(Enumerable.Repeat(value, 16).ToArray());
                RHICommandContext.Current.BlitImages(copy);
                Assert.All(destination[0].ReadPixels(), pixel => Assert.Equal(value, pixel));

            }
        }
        Assert.True(GL.IsFramebuffer(source.FboId));
        Assert.True(GL.IsFramebuffer(destination.FboId));
        Assert.True(GL.IsTexture(source[0].TextureId));
        Assert.True(GL.IsTexture(destination[0].TextureId));
        Assert.Equal(ErrorCode.NoError, GL.GetError());
    }
    /// <summary>Depth-only blits preserve color routing and copy depth without requiring color scratch attachments.</summary>
    [Fact]
    public void DepthOnlyBlitCopiesDepth()
    {
        EnsureContextValid();
        using var scope = OpenGlStateCache.Current.BindFramebufferScope();
        using var state = LegacyFixedFunctionReference.Capture(OpenGlStateCache.Current);
        using var sourceDepth = new DepthTexture(2, 2, PixelInternalFormat.DepthComponent24);
        using var destinationDepth = new DepthTexture(2, 2, PixelInternalFormat.DepthComponent24);
        using var source = DynamicGpuFramebuffer.CreateDepthOnly(sourceDepth)!;
        using var destination = DynamicGpuFramebuffer.CreateDepthOnly(destinationDepth)!;
        GL.Disable(EnableCap.ScissorTest);
        GL.DepthMask(true);
        source.Bind();
        GL.ClearDepth(0.375);
        GL.Clear(ClearBufferMask.DepthBufferBit);
        destination.Bind();
        GL.ClearDepth(1);
        GL.Clear(ClearBufferMask.DepthBufferBit);
        var copy = FramebufferTestSetup.DescribeCopy(source, destination, ImageAspects.Depth);
        RHICommandContext.Current.BlitImages(copy);
        Assert.All(destinationDepth.ReadPixels(), value => Assert.InRange(value, 0.374f, 0.376f));
        Assert.Equal(ErrorCode.NoError, GL.GetError());
    }
    /// <summary>A combined blit copies color through scratch attachments and depth through the original targets.</summary>
    [Fact]
    public void CombinedBlitCopiesColorAndDepth()
    {
        EnsureContextValid();
        using var scope = OpenGlStateCache.Current.BindFramebufferScope();
        using var state = LegacyFixedFunctionReference.Capture(OpenGlStateCache.Current);
        using var sourceDepth = new DepthTexture(2, 2, PixelInternalFormat.DepthComponent24);
        using var destinationDepth = new DepthTexture(2, 2, PixelInternalFormat.DepthComponent24);
        using var sourceColor = RHICommandContext.Current.CreateDynamicTexture2D(new RHITextureDescription { Dimension = TextureDimension.Two, Format = TextureFormat.Rgba32f, Width = System.Math.Max(1, 2), Height = System.Math.Max(1, 2), MipLevels = 1, Storage = ResourceStorageMode.Mutable, Filter = TextureFilterMode.Nearest, BaseLevel = 0, MaxLevel = (1) - 1, Label = null }).RequireResource();
        using var destinationColor = RHICommandContext.Current.CreateDynamicTexture2D(new RHITextureDescription { Dimension = TextureDimension.Two, Format = TextureFormat.Rgba32f, Width = System.Math.Max(1, 2), Height = System.Math.Max(1, 2), MipLevels = 1, Storage = ResourceStorageMode.Mutable, Filter = TextureFilterMode.Nearest, BaseLevel = 0, MaxLevel = (1) - 1, Label = null }).RequireResource();
        using var source = DynamicGpuFramebuffer.CreateSingle(sourceColor)!;
        using var destination = DynamicGpuFramebuffer.CreateSingle(destinationColor)!;
        source.Attach(sourceDepth);
        destination.Attach(destinationDepth);
        sourceColor.UploadDataImmediate(Enumerable.Repeat(5f, 16).ToArray());
        GL.Disable(EnableCap.ScissorTest);
        GL.DepthMask(true);
        source.Bind();
        GL.ClearDepth(0.625);
        GL.Clear(ClearBufferMask.DepthBufferBit);
        GL.DrawBuffer(DrawBufferMode.None); OpenGlStateCache.Current.Invalidate(EPipelineState.FramebufferRouting);
        GL.ReadBuffer(ReadBufferMode.None);
        destination.Bind();
        GL.DrawBuffer(DrawBufferMode.None); OpenGlStateCache.Current.Invalidate(EPipelineState.FramebufferRouting);
        GL.ReadBuffer(ReadBufferMode.None);
        var colorCopy = FramebufferTestSetup.DescribeCopy(source, destination);
        var depthCopy = FramebufferTestSetup.DescribeCopy(source, destination, ImageAspects.Depth);
        RHICommandContext.Current.BlitImages(colorCopy);
        RHICommandContext.Current.BlitImages(depthCopy);
        Assert.Equal((int)DrawBufferMode.None, GL.GetInteger(GetPName.DrawBuffer0));
        Assert.Equal((int)ReadBufferMode.None, GL.GetInteger(GetPName.ReadBuffer));
        Assert.All(destinationDepth.ReadPixels(), value => Assert.InRange(value, 0.624f, 0.626f));
        Assert.All(destinationColor.ReadPixels(), value => Assert.Equal(5f, value));
        Assert.Equal(ErrorCode.NoError, GL.GetError());
    }

    /// <summary>Scratch color blits support borrowed renderbuffer attachments as well as textures.</summary>
    [Fact]
    public void BlitCopiesBorrowedColorRenderbuffer()
    {
        EnsureContextValid();
        using var scope = OpenGlStateCache.Current.BindFramebufferScope();
        using var state = LegacyFixedFunctionReference.Capture(OpenGlStateCache.Current);
        using var color = RHICommandContext.Current.CreateRenderbuffer(new RHIRenderbufferDescription { Format = TextureFormat.Rgba32f, Width = 2, Height = 2, Samples = 0, Label = null }).RequireResource();
        using var source = DynamicGpuFramebuffer.CreateEmpty("Tests.ColorRenderbuffer");
        using var destination = CreateRenderTarget(2, 2, PixelInternalFormat.Rgba32f);
        source.Bind();
        GL.FramebufferRenderbuffer(FramebufferTarget.Framebuffer, FramebufferAttachment.ColorAttachment0,
            RenderbufferTarget.Renderbuffer, color.RenderbufferId);
        GL.DrawBuffer(DrawBufferMode.ColorAttachment0); OpenGlStateCache.Current.Invalidate(EPipelineState.FramebufferRouting);
        GL.ReadBuffer(ReadBufferMode.None);
        GL.Disable(EnableCap.ScissorTest);
        GL.ColorMask(true, true, true, true);
        GL.ClearBuffer(ClearBuffer.Color,0,new[]{.25f,.25f,.25f,.25f});
        // The externally owned framebuffer has no managed attachment metadata.
        using var borrowed = DynamicGpuFramebuffer.Wrap(source.FboId, width: 2, height: 2);
        var copy = new RHIImageBlit(RHIAttachment.FromRenderbuffer(color),new(0,0,2,2),
            FramebufferTestSetup.Select(destination.GetAttachment(FramebufferAttachment.ColorAttachment0)!),new(0,0,2,2));
        RHICommandContext.Current.BlitImages(copy);
        Assert.Equal((int)ReadBufferMode.None, GL.GetInteger(GetPName.ReadBuffer));
        Assert.All(destination[0].ReadPixels(), value => Assert.Equal(0.25f, value));
        destination.Dispose();
        Assert.True(GL.IsRenderbuffer(color.RenderbufferId));
        Assert.Equal(ErrorCode.NoError, GL.GetError());
    }
    /// <summary>Restores cache-bound or engine-owned executables after nested work throws, and submits owned inputs again.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void BoundaryRestoresExecutableOnExceptionalExit(bool engineOwned)
    {
        EnsureContextValid();
        using var programs = new ComponentShaderPrograms();
        var previous = programs.Create<RasterDepthCopyShaderProgram>();
        var nested = programs.Create<RasterDepthCopyShaderProgram>();
        using var source = RHICommandContext.Current.CreateTexture2D(new() { Dimension=TextureDimension.Two, Width=1, Height=1, MipLevels=1, Format=TextureFormat.R32f, Storage=ResourceStorageMode.Mutable }).RequireResource();
        nested.PrimaryDepth = source;
        using var pipeline = ShaderBindingTestPipeline.Create(nested);
        if (engineOwned) OpenGLProgramSelection.Select(OpenGlStateCache.Current, previous.ProgramId);
        else { GL.UseProgram(previous.ProgramId); OpenGlStateCache.Current.Invalidate(EPipelineState.Program); }
        var commands = RHICommandContext.Current;
        var expected = new InvalidOperationException("Controlled boundary failure.");
        try
        {
            Assert.Same(expected, Assert.Throws<InvalidOperationException>(() =>
            {
                using var boundary = commands.EnterBoundary(new("Tests.ExceptionalExecutableRestore", true, graphics:[pipeline]));
                boundary.Run(() => { commands.SetPipeline(pipeline); commands.BindResources(nested.CaptureBindings()); throw expected; });
            }));
            Assert.Equal(previous.ProgramId, GL.GetInteger(GetPName.CurrentProgram));
            Assert.True(OpenGlStateCache.Current.TryGetCachedCurrentProgram(out int restored));
            Assert.Equal(previous.ProgramId, restored);
            Assert.Equal(ErrorCode.NoError, GL.GetError());
        }
        finally { OpenGLProgramSelection.Select(OpenGlStateCache.Current, 0); }
    }
    #endregion
}
