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
        // Engine calls may bypass a primed managed cache.
        GlStateCache.Current.BindFramebuffer(FramebufferTarget.Framebuffer, nested.FboId);
        GL.BindFramebuffer(FramebufferTarget.DrawFramebuffer, defaultFramebuffer ? 0 : draw.FboId);
        GL.BindFramebuffer(FramebufferTarget.ReadFramebuffer, read.FboId);
        GL.ReadBuffer(ReadBufferMode.None);
        DrawBuffersEnum[] routing = [DrawBuffersEnum.ColorAttachment2, DrawBuffersEnum.None, DrawBuffersEnum.ColorAttachment0];
        if (defaultFramebuffer) GL.DrawBuffer(DrawBufferMode.Back);
        else GL.DrawBuffers(routing.Length, routing);
        GL.Viewport(1, 2, 7, 9);
        Assert.Throws<InvalidOperationException>((Action)(() =>
        {
            using var scope = GlStateCache.Current.BindFramebufferScope();
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
        Assert.Equal(defaultFramebuffer ? 0 : draw.FboId, GlStateCache.Current.GetCurrentFramebuffer(FramebufferTarget.DrawFramebuffer));
        Assert.Equal(read.FboId, GlStateCache.Current.GetCurrentFramebuffer(FramebufferTarget.ReadFramebuffer));
        int[] viewport = new int[4];
        GL.GetInteger(GetPName.Viewport, viewport);
        Assert.Equal(new[] {1, 2, 7, 9}, viewport);
        Assert.Equal(ErrorCode.NoError, GL.GetError());
        GpuFramebuffer.Unbind();
    }

    /// <summary>Retains established MRT routing and clears independently authored floating-point attachments.</summary>
    [Fact]
    public void ExistingBindAndClearMethodsClearAllFloatOutputs()
    {
        EnsureContextValid();
        using var state = GlStateCache.Current.CaptureLegacyFixedFunctionState();
        using var target = CreateMRTRenderTarget(2, 2, PixelInternalFormat.Rgba32f, PixelInternalFormat.R32f, PixelInternalFormat.R32f);
        target[0].UploadDataImmediate(Enumerable.Repeat(2f, 16).ToArray());
        target[1].UploadDataImmediate(Enumerable.Repeat(3f, 4).ToArray());
        target[2].UploadDataImmediate(Enumerable.Repeat(17f, 4).ToArray());
        GL.Disable(EnableCap.ScissorTest);
        GL.ColorMask(true, true, true, true);
        target.BindWithViewport();
        target.Clear(0, 0, 0, 0);
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
        using var scope = GlStateCache.Current.BindFramebufferScope();
        using var target = CreateRenderTarget(3, 5, PixelInternalFormat.Rgba32f);
        using (var borrowed = GpuFramebuffer.Wrap(target.FboId, "Borrowed test target", width: 3, height: 5))
        {
            Assert.Equal(3, borrowed.Width);
            Assert.Equal(5, borrowed.Height);
            borrowed.BindWithViewport();
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
        using var scope = GlStateCache.Current.BindFramebufferScope();
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
            GL.DrawBuffers(routing.Length, routing);
            GL.ReadBuffer(ReadBufferMode.ColorAttachment1);
        }
        GL.BindFramebuffer(FramebufferTarget.DrawFramebuffer, callerDraw.FboId);
        GL.BindFramebuffer(FramebufferTarget.ReadFramebuffer, callerRead.FboId);
        GL.ReadBuffer(ReadBufferMode.None);
        GL.Viewport(1, 2, 3, 4);
        using var wrappedSource = GpuFramebuffer.Wrap(source.FboId, width: 2, height: 2);
        using var wrappedDestination = GpuFramebuffer.Wrap(destination.FboId, width: 2, height: 2);
        using var blitter = new GpuFramebufferBlitter(wrapped ? wrappedSource : source, wrapped ? wrappedDestination : destination);
        blitter.Blit();
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
        using var scope = GlStateCache.Current.BindFramebufferScope();
        using var first = DynamicTexture2D.Create(2, 2, PixelInternalFormat.Rgba32f);
        using var replacement = DynamicTexture2D.Create(2, 2, PixelInternalFormat.Rgba32f);
        using var output = DynamicTexture2D.Create(2, 2, PixelInternalFormat.Rgba32f);
        first.UploadDataImmediate(Enumerable.Repeat(3f, 16).ToArray());
        replacement.UploadDataImmediate(Enumerable.Repeat(9f, 16).ToArray());
        using (var source = GpuFramebuffer.CreateSingle(first)!)
        using (var destination = GpuFramebuffer.CreateSingle(output)!)
        {
            using var blitter = new GpuFramebufferBlitter(source, destination);
            blitter.Blit();
            Assert.All(output.ReadPixels(), value => Assert.Equal(3f, value));
            source.Attach(replacement);
            blitter.Blit();
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
        using var scope = GlStateCache.Current.BindFramebufferScope();
        using var source = CreateRenderTarget(2, 2, PixelInternalFormat.Rgba32f);
        using var destination = CreateRenderTarget(2, 2, PixelInternalFormat.Rgba32f);
        using (var blitter = new GpuFramebufferBlitter(source, destination))
        {
            // Repeated blits must continue to copy the current source data.
            foreach (float value in new[] { 3f, 7f, 11f })
            {
                source[0].UploadDataImmediate(Enumerable.Repeat(value, 16).ToArray());
                blitter.Blit();
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
        using var scope = GlStateCache.Current.BindFramebufferScope();
        using var state = GlStateCache.Current.CaptureLegacyFixedFunctionState();
        using var sourceDepth = new DepthTexture(2, 2, PixelInternalFormat.DepthComponent24);
        using var destinationDepth = new DepthTexture(2, 2, PixelInternalFormat.DepthComponent24);
        using var source = GpuFramebuffer.CreateDepthOnly(sourceDepth)!;
        using var destination = GpuFramebuffer.CreateDepthOnly(destinationDepth)!;
        GL.Disable(EnableCap.ScissorTest);
        GL.DepthMask(true);
        source.Bind();
        GL.ClearDepth(0.375);
        source.Clear(ClearBufferMask.DepthBufferBit);
        destination.Bind();
        GL.ClearDepth(1);
        destination.Clear(ClearBufferMask.DepthBufferBit);
        using var blitter = new GpuFramebufferBlitter(source, destination, ClearBufferMask.DepthBufferBit);
        blitter.Blit();
        Assert.All(destinationDepth.ReadPixels(), value => Assert.InRange(value, 0.374f, 0.376f));
        Assert.Equal(ErrorCode.NoError, GL.GetError());
    }
    /// <summary>A combined blit copies color through scratch attachments and depth through the original targets.</summary>
    [Fact]
    public void CombinedBlitCopiesColorAndDepth()
    {
        EnsureContextValid();
        using var scope = GlStateCache.Current.BindFramebufferScope();
        using var state = GlStateCache.Current.CaptureLegacyFixedFunctionState();
        using var sourceDepth = new DepthTexture(2, 2, PixelInternalFormat.DepthComponent24);
        using var destinationDepth = new DepthTexture(2, 2, PixelInternalFormat.DepthComponent24);
        using var sourceColor = DynamicTexture2D.Create(2, 2, PixelInternalFormat.Rgba32f);
        using var destinationColor = DynamicTexture2D.Create(2, 2, PixelInternalFormat.Rgba32f);
        using var source = GpuFramebuffer.CreateSingle(sourceColor)!;
        using var destination = GpuFramebuffer.CreateSingle(destinationColor)!;
        source.Attach(sourceDepth);
        destination.Attach(destinationDepth);
        sourceColor.UploadDataImmediate(Enumerable.Repeat(5f, 16).ToArray());
        GL.Disable(EnableCap.ScissorTest);
        GL.DepthMask(true);
        source.Bind();
        GL.ClearDepth(0.625);
        source.Clear(ClearBufferMask.DepthBufferBit);
        GL.DrawBuffer(DrawBufferMode.None);
        GL.ReadBuffer(ReadBufferMode.None);
        destination.Bind();
        GL.DrawBuffer(DrawBufferMode.None);
        GL.ReadBuffer(ReadBufferMode.None);
        using var blitter = new GpuFramebufferBlitter(source, destination, ClearBufferMask.ColorBufferBit | ClearBufferMask.DepthBufferBit);
        blitter.Blit();
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
        using var scope = GlStateCache.Current.BindFramebufferScope();
        using var state = GlStateCache.Current.CaptureLegacyFixedFunctionState();
        using var color = GpuRenderbuffer.Create(RenderbufferStorage.Rgba32f, 2, 2);
        using var source = GpuFramebuffer.CreateEmpty("Tests.ColorRenderbuffer");
        using var destination = CreateRenderTarget(2, 2, PixelInternalFormat.Rgba32f);
        source.Bind();
        GL.FramebufferRenderbuffer(FramebufferTarget.Framebuffer, FramebufferAttachment.ColorAttachment0,
            RenderbufferTarget.Renderbuffer, color.RenderbufferId);
        GL.DrawBuffer(DrawBufferMode.ColorAttachment0);
        GL.ReadBuffer(ReadBufferMode.None);
        GL.Disable(EnableCap.ScissorTest);
        GL.ColorMask(true, true, true, true);
        source.Clear(0.25f, 0.25f, 0.25f, 0.25f);
        // The externally owned framebuffer has no managed attachment metadata.
        using var borrowed = GpuFramebuffer.Wrap(source.FboId, width: 2, height: 2);
        using var blitter = new GpuFramebufferBlitter(borrowed, destination);
        blitter.Blit();
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
    public void UseScopeRestoresOwnershipOnExceptionalExit(bool engineOwned)
    {
        EnsureContextValid();
        using var programs = new ComponentShaderPrograms();
        var previous = programs.Create<GpuProgramUseScopeTests.CountingShader>();
        var nested = programs.Create<GpuProgramUseScopeTests.CountingShader>();
        if (engineOwned) previous.Use();
        else GlStateCache.Current.UseProgram(previous.ProgramId);
        Assert.Throws<InvalidOperationException>((Action)(() =>
        {
            using var activation = nested.UseScope();
            Assert.Same(nested, ShaderProgramBase.CurrentShaderProgram);
            throw new InvalidOperationException("Controlled nested failure.");
        }));
        Assert.Equal(previous.ProgramId, GL.GetInteger(GetPName.CurrentProgram));
        Assert.Equal(engineOwned ? previous : null, ShaderProgramBase.CurrentShaderProgram);
        if (engineOwned) Assert.Equal(2, previous.Submissions);
        Assert.Equal(ErrorCode.NoError, GL.GetError());
        if (engineOwned) previous.Stop();
        else GlStateCache.Current.UseProgram(0);
        GlStateCache.Current.InvalidateAll();
    }
    #endregion
}
