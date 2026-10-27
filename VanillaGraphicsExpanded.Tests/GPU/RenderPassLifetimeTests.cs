using System;
using System.Linq;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Rendering.RHI;
using VanillaGraphicsExpanded.Tests.GPU.Fixtures;

namespace VanillaGraphicsExpanded.Tests.GPU;

/// <summary>Checks attachment intention realization, private target reuse, mutation guards and cleanup through the final context.</summary>
[Collection("GPU")]
[Trait("Category", "GPU")]
public sealed class RenderPassLifetimeTests(HeadlessGLFixture fixture) : RenderTestBase(fixture)
{
    #region Public API
    /// <summary>Load actions do not change target identity; replacing the same storage owner invalidates its realization.</summary>
    [Fact]
    public void RealizationsReuseRoutingAndFollowStorageReplacement()
    {
        EnsureContextValid();
        var commands = RHICommandContext.Current;
        using var image = CreateImage(4, 4);
        var preserve = new RHIRenderPassDescription([new(Image: RHIAttachment.FromTexture(image))]);
        var clear = new RHIRenderPassDescription([new(Image: RHIAttachment.FromTexture(image), Load: AttachmentLoad.Clear,
            Clear: ColorClearValue.Float(.25f, .5f, .75f, 1))]);
        int original = 0;
        using (var boundary = commands.EnterBoundary(new("Tests.TargetReuse", true, operations: BoundaryOperations.RenderPasses)))
        boundary.Run(() =>
        {
            commands.BeginRenderPass(preserve);
            original = GL.GetInteger(GetPName.DrawFramebufferBinding);
            Assert.Equal(4, commands.PassViewport.Width);
            commands.EndRenderPass();
            commands.BeginRenderPass(clear);
            Assert.Equal(original, GL.GetInteger(GetPName.DrawFramebufferBinding));
            commands.EndRenderPass();
        });
        Assert.Equal(new float[] { .25f, .5f, .75f, 1 }, image.ReadPixels().Take(4));
        Assert.True(image.Resize(7, 3));
        using var next = commands.EnterBoundary(new("Tests.TargetReplacement", true, operations: BoundaryOperations.RenderPasses));
        next.Run(() =>
        {
            commands.BeginRenderPass(preserve);
            Assert.Equal(7, commands.PassViewport.Width); Assert.Equal(3, commands.PassViewport.Height);
            Assert.NotEqual(original, GL.GetInteger(GetPName.DrawFramebufferBinding));
            commands.EndRenderPass();
        });
    }

    /// <summary>Pass guards hold original image storage on success and failure without transferring its ownership.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ActiveBorrowGuardsMutationsAndReleasesOnBoundaryExit(bool fail)
    {
        EnsureContextValid();
        var commands = RHICommandContext.Current;
        using var image = CreateImage(4, 4);
        var pass = new RHIRenderPassDescription([new(Image: RHIAttachment.FromTexture(image))]);
        var expected = new ArithmeticException("fixture body");
        void Execute()
        {
            using var boundary = commands.EnterBoundary(new("Tests.TargetBorrow", true, operations: BoundaryOperations.RenderPasses));
            boundary.Run(() =>
            {
                commands.BeginRenderPass(pass);
                Assert.Throws<InvalidOperationException>(() => image.Resize(5, 5));
                Assert.Throws<InvalidOperationException>(image.Dispose);
                Assert.Throws<InvalidOperationException>(() => image.Detach());
                Assert.Throws<InvalidOperationException>(() => commands.BeginRenderPass(pass));
                if (fail) throw expected;
                commands.EndRenderPass();
            });
        }
        if (fail) Assert.Same(expected, Assert.Throws<ArithmeticException>(Execute)); else Execute();
        Assert.False(commands.HasRenderPass);
        Assert.True(image.IsValid);
        Assert.True(image.Resize(5, 5));
    }

    /// <summary>Retired original references and bad clear/area intentions fail before any incoming selector or contents change.</summary>
    [Fact]
    public void InvalidSetupPreservesContentsAndBindings()
    {
        EnsureContextValid();
        var commands = RHICommandContext.Current;
        using var image = CreateImage(4, 4);
        image.UploadDataImmediate(Enumerable.Repeat(.375f, 64).ToArray());
        using var incoming = CreateRenderTarget(2, 2, PixelInternalFormat.Rgba8);
        incoming.Bind();
        var wrongClear = new RHIRenderPassDescription([new(Image: RHIAttachment.FromTexture(image),
            Load: AttachmentLoad.Clear, Clear: ColorClearValue.Int(1, 2, 3, 4))]);
        var wrongArea = new RHIRenderPassDescription([new(Image: RHIAttachment.FromTexture(image))], area: new(3, 3, 2, 2));
        var valid = new RHIRenderPassDescription([new(Image: RHIAttachment.FromTexture(image))]);
        using (var boundary = commands.EnterBoundary(new("Tests.TargetFailure", true, operations: BoundaryOperations.RenderPasses)))
        boundary.Run(() =>
        {
            Assert.Throws<ArgumentException>(() => commands.BeginRenderPass(wrongClear));
            Assert.Throws<ArgumentOutOfRangeException>(() => commands.BeginRenderPass(wrongArea));
            Assert.Equal(incoming.FboId, GL.GetInteger(GetPName.DrawFramebufferBinding));
            commands.BeginRenderPass(valid); commands.EndRenderPass();
        });
        Assert.All(image.ReadPixels(), value => Assert.Equal(.375f, value));
        image.Dispose();
        using var rejected = commands.EnterBoundary(new("Tests.RetiredTarget", true, operations: BoundaryOperations.RenderPasses));
        rejected.Run(() => Assert.Throws<InvalidOperationException>(() => commands.BeginRenderPass(valid)));
        Assert.True(incoming.Resize(3, 3));
    }

    /// <summary>External targets require explicit metadata publication and restore their original sparse route at boundary exit.</summary>
    [Fact]
    public void ExternalSurfaceRestoresIndependentRoutingAndBindings()
    {
        EnsureContextValid();
        var commands = RHICommandContext.Current;
        using var target = CreateMRTRenderTarget(4, 4, PixelInternalFormat.Rgba8, PixelInternalFormat.Rgba8);
        using var incoming = CreateRenderTarget(2, 2, PixelInternalFormat.Rgba8);
        target.Bind();
        GL.DrawBuffers(3, [DrawBuffersEnum.None, DrawBuffersEnum.ColorAttachment1, DrawBuffersEnum.ColorAttachment0]);
        StateCache.Current.Invalidate(EPipelineState.FramebufferRouting);
        incoming.Bind();
        using var surface = DynamicGpuFramebuffer.Wrap(target.FboId, width: 4, height: 4);
        var pass = new RHIRenderPassDescription(surface.SurfaceReference, [new(AttachmentIndex: 0), new(AttachmentIndex: 1)]);
        using (var boundary = commands.EnterBoundary(new("Tests.SurfaceAdmission", true, operations: BoundaryOperations.RenderPasses)))
            boundary.Run(() => Assert.Throws<InvalidOperationException>(() => commands.BeginRenderPass(pass)));
        surface.PublishRenderPassMetadata();
        using (var boundary = commands.EnterBoundary(new("Tests.SurfaceRestore", true, operations: BoundaryOperations.RenderPasses)))
            boundary.Run(() => { commands.BeginRenderPass(pass); Assert.Equal(target.FboId, GL.GetInteger(GetPName.DrawFramebufferBinding)); });
        Assert.False(commands.HasRenderPass);
        Assert.Equal(incoming.FboId, GL.GetInteger(GetPName.DrawFramebufferBinding));
        target.Bind();
        Assert.Equal((int)DrawBuffersEnum.None, GL.GetInteger(GetPName.DrawBuffer0));
        Assert.Equal((int)DrawBuffersEnum.ColorAttachment1, GL.GetInteger(GetPName.DrawBuffer1));
        Assert.Equal((int)DrawBuffersEnum.ColorAttachment0, GL.GetInteger(GetPName.DrawBuffer2));
    }

    /// <summary>Incompatible layered target creation leaves no active pass, leaked storage guard or changed incoming framebuffer.</summary>
    [Fact]
    public void IncompleteCandidateDoesNotPublish()
    {
        EnsureContextValid();
        var commands = RHICommandContext.Current;
        using var layered = commands.CreateTexture(new()
        {
            Dimension = TextureDimension.TwoArray, Width = 4, Height = 4, Depth = 2,
            Format = TextureFormat.Rgba8, MipLevels = 1, Storage = ResourceStorageMode.Immutable
        }).RequireResource();
        using var flat = CreateImage(4, 4, TextureFormat.Rgba8);
        using var incoming = CreateRenderTarget(2, 2, PixelInternalFormat.Rgba8);
        incoming.Bind();
        var pass = new RHIRenderPassDescription([new(Image: RHIAttachment.FromTexture(layered, layered: true)), new(Image: RHIAttachment.FromTexture(flat))]);
        using var boundary = commands.EnterBoundary(new("Tests.IncompleteTarget", true, operations: BoundaryOperations.RenderPasses));
        boundary.Run(() =>
        {
            Assert.Throws<InvalidOperationException>(() => commands.BeginRenderPass(pass));
            Assert.False(commands.HasRenderPass);
            Assert.Equal(incoming.FboId, GL.GetInteger(GetPName.DrawFramebufferBinding));
        });
        Assert.True(flat.Resize(5, 5));
    }

    /// <summary>Published target compatibility reports the realized sample count without another descriptor query.</summary>
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public void SignatureUsesRealizedRenderbufferSamples(int requested)
    {
        EnsureContextValid();
        var commands = RHICommandContext.Current;
        using var image = commands.CreateRenderbuffer(new() { Format = TextureFormat.Rgba8, Width = 4, Height = 4, Samples = requested }).RequireResource();
        var pass = new RHIRenderPassDescription([new(Image: RHIAttachment.FromRenderbuffer(image))]);
        int actual;
        using (StateCache.Current.BindRenderbufferScope(image.RenderbufferId)) GL.GetRenderbufferParameter(RenderbufferTarget.Renderbuffer, RenderbufferParameterName.RenderbufferSamples, out actual);
        Assert.Equal(Math.Max(1, actual), commands.DescribeRenderTargets(pass).Samples);
    }
    #endregion

    #region Private
    /// <summary>Creates independently owned mutable image storage for pass lifetime checks.</summary>
    private static DynamicTexture2D CreateImage(int width, int height, TextureFormat format = TextureFormat.Rgba32f)
        => RHICommandContext.Current.CreateDynamicTexture2D(new()
        {
            Dimension = TextureDimension.Two, Width = width, Height = height, Format = format,
            MipLevels = 1, Storage = ResourceStorageMode.Mutable, BaseLevel = 0, MaxLevel = 0
        }).RequireResource();
    #endregion
}
