using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.LumOn;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Rendering.Pipeline;
using VanillaGraphicsExpanded.Rendering.Pipeline.Descriptions;
using VanillaGraphicsExpanded.Rendering.Pipeline.Passes;
using VanillaGraphicsExpanded.Rendering.Shaders;
using VanillaGraphicsExpanded.Tests.GPU.Fixtures;

namespace VanillaGraphicsExpanded.Tests.GPU;

/// <summary>Verifies pass borrowing, target compatibility, mutation guards, and failed setup cleanup.</summary>
[Collection("GPU")]
[Trait("Category", "GPU")]
public sealed class RenderPassLifetimeTests(HeadlessGLFixture fixture) : RenderTestBase(fixture)
{
    #region Public API
    /// <summary>A retained pipeline accepts different IDs and dimensions but rejects format and sample changes.</summary>
    [Fact]
    public void PipelineReusesCompatibleTargetsAndRefreshesDimensions()
    {
        EnsureContextValid();
        using var programs = new ComponentShaderPrograms();
        var shader = programs.Create<LumOnUpsampleShaderProgram>();
        using var lifetime = new GraphicsPipelineLifetime();
        using var pipeline = new GraphicsPipeline(lifetime, new(shader.GraphicsIdentity!,
            new([new(0, 3, VertexAttribPointerType.Float, VertexInterpretation.Floating, 0, 0, 12)]),
            new([new(PixelInternalFormat.Rgba16f)]), DynamicPipelineState.Viewport), shader);
        using var first = CreateRenderTarget(2, 2, PixelInternalFormat.Rgba16f);
        using var second = CreateRenderTarget(5, 3, PixelInternalFormat.Rgba16f);
        RenderPassTestBoundary.Run(boundary =>
        {
            using (var pass = RenderPass.Begin(new(first, [new(0)]), boundary)) pass.ValidatePipeline(pipeline);
            using (var pass = RenderPass.Begin(new(second, [new(0)]), boundary))
            {
                pass.ValidatePipeline(pipeline); Assert.Equal(5, pass.Area.Width); Assert.Equal(3, pass.Viewport.Height);
            }
        });
        Assert.True(first.Resize(7, 4));
        RenderPassTestBoundary.Run(boundary =>
        {
            using var pass = RenderPass.Begin(new(first, [new(0)]), boundary);
            pass.ValidatePipeline(pipeline); Assert.Equal(7, pass.Area.Width); Assert.Equal(4, pass.Viewport.Height);
        });
        using var replacementStorage = DynamicTexture2D.Create(7, 4, PixelInternalFormat.Rgba32f);
        using var replacement = GpuFramebufferAttachment.FromTexture(replacementStorage);
        first.SetAttachment(FramebufferAttachment.ColorAttachment0, replacement);
        RenderPassTestBoundary.Run(boundary =>
        {
            using var pass = RenderPass.Begin(new(first, [new(0)]), boundary);
            Assert.Throws<InvalidOperationException>(() => pass.ValidatePipeline(pipeline));
        });
        using var multisampleStorage = GpuRenderbuffer.Create(RenderbufferStorage.Rgba16f, 7, 4, samples: 4);
        using var multisample = GpuFramebufferAttachment.FromRenderbuffer(multisampleStorage);
        first.SetAttachment(FramebufferAttachment.ColorAttachment0, multisample);
        RenderPassTestBoundary.Run(boundary =>
        {
            using var pass = RenderPass.Begin(new(first, [new(0)]), boundary);
            Assert.Throws<InvalidOperationException>(() => pass.ValidatePipeline(pipeline));
        });
        Assert.Equal(ErrorCode.NoError, GL.GetError());
    }

    /// <summary>Active passes reject mutation and release all borrowing on normal and exceptional exits.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ActiveBorrowGuardsMutationsAndEndsWithoutDisposal(bool fail)
    {
        EnsureContextValid();
        using var texture = DynamicTexture2D.Create(4, 4, PixelInternalFormat.Rgba8);
        using var target = GpuFramebuffer.CreateSingle(texture)!;
        using var replacementStorage = DynamicTexture2D.Create(4, 4, PixelInternalFormat.Rgba8);
        using var replacement = GpuFramebufferAttachment.FromTexture(replacementStorage);
        var expected = new InvalidOperationException("fixture body");
        void Run() => RenderPassTestBoundary.Run(boundary =>
        {
            using var pass = RenderPass.Begin(new(target, [new(0)]), boundary);
            Assert.Throws<InvalidOperationException>(() => target.Resize(5, 5));
            Assert.Throws<InvalidOperationException>(() => texture.Resize(5, 5));
            Assert.Throws<InvalidOperationException>(() => target.SetAttachment(FramebufferAttachment.ColorAttachment0, replacement));
            Assert.Throws<InvalidOperationException>(() => target.RemoveAttachment(FramebufferAttachment.ColorAttachment0));
            Assert.Throws<InvalidOperationException>(target.Dispose);
            Assert.Throws<InvalidOperationException>(texture.Dispose);
            Assert.Throws<InvalidOperationException>(() => target.Detach());
            Assert.Throws<InvalidOperationException>(() => texture.Detach());
            Assert.Throws<InvalidOperationException>(target.GetAttachment(FramebufferAttachment.ColorAttachment0)!.Dispose);
            Assert.Throws<InvalidOperationException>(() => RenderPass.Begin(new(target, [new(0)]), boundary));
            pass.Validate();
            if (fail) throw expected;
        });
        if (fail) Assert.Same(expected, Assert.Throws<InvalidOperationException>(Run)); else Run();
        Assert.True(target.IsValid); Assert.True(texture.IsValid); Assert.True(GL.IsTexture(texture.TextureId));
        Assert.True(target.Resize(5, 5));
        Assert.Equal(ErrorCode.NoError, GL.GetError());
    }

    /// <summary>Retired targets and images fail before use, and failed setup cannot retain bindings or borrow guards.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void RetirementAndSetupFailuresAreIsolated(bool retireImage)
    {
        EnsureContextValid();
        using var target = CreateRenderTarget(4, 4, PixelInternalFormat.Rgba8);
        using var incoming = CreateRenderTarget(2, 2, PixelInternalFormat.Rgba8);
        incoming.Bind();
        if (retireImage) target[0].Dispose(); else target.Dispose();
        RenderPassTestBoundary.Run(boundary =>
        {
            if (retireImage) Assert.Throws<InvalidOperationException>(() => RenderPass.Begin(new(target, [new(0)]), boundary));
            else Assert.Throws<ObjectDisposedException>(() => RenderPass.Begin(new(target, [new(0)]), boundary));
            Assert.Equal(incoming.FboId, GL.GetInteger(GetPName.DrawFramebufferBinding));
            using var valid = RenderPass.Begin(new(incoming, [new(0)]), boundary);
            valid.Validate();
        });
        Assert.True(incoming.Resize(3, 3));
        Assert.Equal(ErrorCode.NoError, GL.GetError());
    }

    /// <summary>External framebuffer metadata must be published and republished at its actual replacement boundary.</summary>
    [Fact]
    public void WrappedMetadataRequiresExplicitRefreshAndDefaultIsRejected()
    {
        EnsureContextValid();
        using var source = CreateRenderTarget(4, 4, PixelInternalFormat.Rgba8);
        using var wrapped = GpuFramebuffer.Wrap(source.FboId, width: 4, height: 4);
        RenderPassTestBoundary.Run(boundary => Assert.Throws<InvalidOperationException>(() => RenderPass.Begin(new(wrapped, [new(0)]), boundary)));
        wrapped.PublishRenderPassMetadata();
        RenderPassTestBoundary.Run(boundary => { using var pass = RenderPass.Begin(new(wrapped, [new(0)]), boundary); pass.Validate(); });
        Assert.True(source.Resize(6, 3));
        wrapped.RefreshWrappedFramebuffer(source.FboId, 6, 3);
        RenderPassTestBoundary.Run(boundary => Assert.Throws<InvalidOperationException>(() => RenderPass.Begin(new(wrapped, [new(0)]), boundary)));
        wrapped.PublishRenderPassMetadata();
        RenderPassTestBoundary.Run(boundary =>
        {
            using var pass = RenderPass.Begin(new(wrapped, [new(0)]), boundary);
            Assert.Equal(6, pass.Area.Width); Assert.Equal(3, pass.Area.Height);
        });
        using var defaultTarget = GpuFramebuffer.Wrap(0, width: 4, height: 4);
        RenderPassTestBoundary.Run(boundary => Assert.Throws<NotSupportedException>(() => RenderPass.Begin(new(defaultTarget, [new(0)]), boundary)));
        Assert.Equal(ErrorCode.NoError, GL.GetError());
    }

    /// <summary>Bad clear types and render extents fail without modifying contents or blocking a subsequent valid pass.</summary>
    [Fact]
    public void InvalidSetupPreservesContentsAndBindings()
    {
        EnsureContextValid();
        using var target = CreateRenderTarget(4, 4, PixelInternalFormat.Rgba32f);
        using var incoming = CreateRenderTarget(2, 2, PixelInternalFormat.Rgba8);
        target[0].UploadDataImmediate(Enumerable.Repeat(.375f, 64).ToArray());
        incoming.Bind();
        RenderPassTestBoundary.Run(boundary =>
        {
            Assert.Throws<ArgumentException>(() => RenderPass.Begin(new(target,
                [new(0, AttachmentLoad.Clear, Clear: ColorClearValue.Int(1, 2, 3, 4))]), boundary));
            Assert.Throws<ArgumentOutOfRangeException>(() => RenderPass.Begin(new(target, [new(0)], area: new(3, 3, 2, 2)), boundary));
            Assert.Equal(incoming.FboId, GL.GetInteger(GetPName.DrawFramebufferBinding));
            using var valid = RenderPass.Begin(new(target, [new(0)]), boundary);
            valid.Validate();
        });
        Assert.All(target[0].ReadPixels(), value => Assert.Equal(.375f, value));
        Assert.True(target.Resize(5, 5));
        Assert.Equal(ErrorCode.NoError, GL.GetError());
    }

    /// <summary>Boundary cleanup ends an undisposed pass and restores the target's original sparse routing.</summary>
    [Fact]
    public void BoundaryCleanupRestoresRoutingAndReleasesBorrow()
    {
        EnsureContextValid();
        using var target = CreateMRTRenderTarget(4, 4, PixelInternalFormat.Rgba8, PixelInternalFormat.Rgba8);
        using var incoming = CreateRenderTarget(2, 2, PixelInternalFormat.Rgba8);
        target.Bind(); GL.DrawBuffers(3, [DrawBuffersEnum.None, DrawBuffersEnum.ColorAttachment1, DrawBuffersEnum.ColorAttachment0]);
        incoming.Bind();
        RenderPass? retained = null;
        RenderPassTestBoundary.Run(boundary => retained = RenderPass.Begin(new(target, [new(0), new(1)]), boundary));
        Assert.Throws<ObjectDisposedException>(retained!.Validate);
        Assert.Equal(incoming.FboId, GL.GetInteger(GetPName.DrawFramebufferBinding));
        target.Bind();
        Assert.Equal((int)DrawBuffersEnum.None, GL.GetInteger(GetPName.DrawBuffer0));
        Assert.Equal((int)DrawBuffersEnum.ColorAttachment1, GL.GetInteger(GetPName.DrawBuffer1));
        Assert.Equal((int)DrawBuffersEnum.ColorAttachment0, GL.GetInteger(GetPName.DrawBuffer2));
        Assert.True(target[0].Resize(6, 5)); Assert.True(target[1].Resize(6, 5));
        RenderPassTestBoundary.Run(boundary =>
        {
            using var pass = RenderPass.Begin(new(target, [new(0), new(1)]), boundary);
            Assert.Equal(6, pass.Area.Width); Assert.Equal(5, pass.Area.Height);
        });
        Assert.Equal(ErrorCode.NoError, GL.GetError());
    }

    /// <summary>Packed storage attached only to depth must not advertise an unattached stencil aspect.</summary>
    [Fact]
    public void PackedStorageSignatureUsesAttachedAspects()
    {
        EnsureContextValid();
        using var storage = GpuRenderbuffer.Create(RenderbufferStorage.Depth24Stencil8, 4, 4);
        using var image = GpuFramebufferAttachment.FromRenderbuffer(storage);
        using var target = GpuFramebuffer.CreateEmpty();
        target.SetAttachment(FramebufferAttachment.DepthAttachment, image);
        RenderPassTestBoundary.Run(boundary =>
        {
            using var pass = RenderPass.Begin(new(target, []), boundary);
            Assert.True(pass.Signature.HasDepth); Assert.False(pass.Signature.HasStencil);
        });
        Assert.Equal(ErrorCode.NoError, GL.GetError());
    }

    /// <summary>Target compatibility uses the driver's realized sample count, including rounded requests and ordinary storage.</summary>
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public void SignatureUsesRealizedRenderbufferSamples(int requestedSamples)
    {
        EnsureContextValid();
        using var storage = GpuRenderbuffer.Create(RenderbufferStorage.Rgba8, 4, 4, samples: requestedSamples);
        using var image = GpuFramebufferAttachment.FromRenderbuffer(storage);
        using var target = GpuFramebuffer.CreateEmpty();
        target.SetAttachment(FramebufferAttachment.ColorAttachment0, image);
        int actual;
        using (storage.BindScope())
            GL.GetRenderbufferParameter(RenderbufferTarget.Renderbuffer, RenderbufferParameterName.RenderbufferSamples, out actual);
        Assert.Equal(actual, storage.Samples);
        Assert.Equal(actual, image.Samples);
        if (requestedSamples == 0) Assert.Equal(0, actual);
        else Assert.True(actual >= requestedSamples);
        RenderPassTestBoundary.Run(boundary =>
        {
            using var pass = RenderPass.Begin(new(target, [new(0)]), boundary);
            Assert.Equal(Math.Max(1, actual), pass.Signature.Samples);
        });
        Assert.Equal(ErrorCode.NoError, GL.GetError());
    }

    /// <summary>Native completeness failure after routing setup restores incoming bindings and releases every borrow.</summary>
    [Fact]
    public void NativeIncompleteSetupRestoresRoutingAndBindings()
    {
        EnsureContextValid();
        using var layered = DynamicTexture3D.Create(4, 4, 2, PixelInternalFormat.Rgba8);
        using var flat = DynamicTexture2D.Create(4, 4, PixelInternalFormat.Rgba8);
        using var original = DynamicTexture2D.Create(4, 4, PixelInternalFormat.Rgba8);
        using var image = GpuFramebufferAttachment.FromTexture(layered);
        using var flatImage = GpuFramebufferAttachment.FromTexture(flat);
        using var originalImage = GpuFramebufferAttachment.FromTexture(original);
        using var target = GpuFramebuffer.CreateEmpty();
        target.SetAttachment(FramebufferAttachment.ColorAttachment0, originalImage);
        target.SetAttachment(FramebufferAttachment.ColorAttachment1, flatImage);
        RenderPassTestBoundary.Run(boundary =>
        {
            using var pass = RenderPass.Begin(new(target, [new(0), new(1)]), boundary);
            pass.Validate();
        });
        // Equal format and dimensions retain pipeline compatibility but must invalidate cached native completeness.
        target.SetAttachment(FramebufferAttachment.ColorAttachment0, image);
        target.Bind(); GL.DrawBuffers(2, [DrawBuffersEnum.ColorAttachment1, DrawBuffersEnum.ColorAttachment0]);
        using var incoming = CreateRenderTarget(2, 2, PixelInternalFormat.Rgba8);
        incoming.Bind();
        RenderPassTestBoundary.Run(boundary =>
        {
            Assert.Throws<InvalidOperationException>(() => RenderPass.Begin(new(target, [new(0), new(1)]), boundary));
            Assert.Equal(incoming.FboId, GL.GetInteger(GetPName.DrawFramebufferBinding));
        });
        target.Bind();
        Assert.Equal((int)DrawBuffersEnum.ColorAttachment1, GL.GetInteger(GetPName.DrawBuffer0));
        Assert.Equal((int)DrawBuffersEnum.ColorAttachment0, GL.GetInteger(GetPName.DrawBuffer1));
        Assert.True(target.RemoveAttachment(FramebufferAttachment.ColorAttachment0));
        Assert.True(flat.Resize(5, 5));
        Assert.Equal(ErrorCode.NoError, GL.GetError());
    }
    #endregion
}
