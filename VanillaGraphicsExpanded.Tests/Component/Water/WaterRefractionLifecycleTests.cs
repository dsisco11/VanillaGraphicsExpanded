using VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;
using VanillaGraphicsExpanded.Rendering.RHI;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.PBR.Liquids;
using VanillaGraphicsExpanded.Rendering;


namespace VanillaGraphicsExpanded.Tests.Component.Water;


/// <summary>Checks immutable source lifetime independently of the selected lighting implementation.</summary>
[Collection("GPU")]
[Trait("Category", "GpuIntegration")]
public sealed class WaterRefractionLifecycleTests(HeadlessGLFixture fixture) : RenderTestBase(fixture)
{


    #region Public API
    /// <summary>Capture owns only its receiver pair and retains sparse output routing across reuse and resize.</summary>
    [Fact]
    public void CaptureTargetOwnsOnlyReceiverAttachments()
    {
        // Arrange: establish this scenario's controlled inputs and starting state.

        EnsureContextValid();
        using var bindings = OpenGlStateCache.Current.BindFramebufferScope();
        using var source = new WaterRefractionScene();
        // Act: begin a receiver capture and inspect the attachment layout it publishes.
        var target = source.BeginCapture(2, 2)!;
        // Assert: the initial capture target routes only its receiver color and depth attachments.
        Assert.IsType<RHIRenderPassDescription>(target);
        Assert.True(target.Colors[0].DiscardOutput);
        Assert.Equal((GpuTextureRef)source.Color!, target.Colors[1].Image!.Value.Texture);
        Assert.Equal((GpuTextureRef)source.Depth!, target.Colors[2].Image!.Value.Texture);
        Assert.Equal(3, target.Colors.Length);
        var context=RHICommandContext.Current;
        // Act: complete the capture pass before publishing its frame.
        using (var boundary=context.EnterBoundary(new("Tests.Water.CaptureTargets",true,operations:BoundaryOperations.RenderPasses)))
            boundary.Run(()=>{ context.BeginRenderPass(target); context.EndRenderPass(); });
        source.Publish();
        // Assert the initial target is published; reuse clears publication, then resize retires both old attachments.
        Assert.True(source.Published);
        Assert.Same(target, source.BeginCapture(2, 2));
        Assert.False(source.Published);
        var oldColor = source.Color;
        var oldDepth = source.Depth;
        Assert.NotSame(target, source.BeginCapture(3, 1));
        Assert.False(oldColor!.IsValid);
        Assert.False(oldDepth!.IsValid);
        Assert.Equal(3, source.Color!.Width);
        Assert.Equal(1, source.Depth!.Height);
        // Act and assert the resized target publishes, rejects an invalid size, and can recover with a fresh target.
        source.Publish();
        Assert.True(source.Published);
        Assert.Null(source.BeginCapture(0, 1));
        Assert.False(source.Published);
        Assert.Null(source.Color);
        Assert.NotNull(source.BeginCapture(2, 2));
        Assert.False(source.Published);
        Assert.Equal(ErrorCode.NoError, GL.GetError());
    }

    /// <summary>Repeated toggles invalidate publication, resize both images together and preserve borrowed color.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void SourceLifetimeSurvivesTogglesResizeAndReload(bool lumon)
    {
        // Arrange: establish this scenario's controlled inputs and starting state.

        EnsureContextValid();
        using var source = new WaterRefractionScene();
        using var composite = RHICommandContext.Current.CreateDynamicTexture2D(new RHITextureDescription { Dimension = TextureDimension.Two, Format = TextureFormat.Rgba16f, Width = System.Math.Max(1, 2), Height = System.Math.Max(1, 2), MipLevels = 1, Storage = ResourceStorageMode.Mutable, Filter = TextureFilterMode.Nearest, BaseLevel = 0, MaxLevel = (1) - 1, Label = null }).RequireResource();
        using var resized = RHICommandContext.Current.CreateDynamicTexture2D(new RHITextureDescription { Dimension = TextureDimension.Two, Format = TextureFormat.Rgba16f, Width = System.Math.Max(1, 3), Height = System.Math.Max(1, 1), MipLevels = 1, Storage = ResourceStorageMode.Mutable, Filter = TextureFilterMode.Nearest, BaseLevel = 0, MaxLevel = (1) - 1, Label = null }).RequireResource();
        using var expanded = RHICommandContext.Current.CreateDynamicTexture2D(new RHITextureDescription { Dimension = TextureDimension.Two, Format = TextureFormat.Rgba16f, Width = System.Math.Max(1, 4), Height = System.Math.Max(1, 2), MipLevels = 1, Storage = ResourceStorageMode.Mutable, Filter = TextureFilterMode.Nearest, BaseLevel = 0, MaxLevel = (1) - 1, Label = null }).RequireResource();
        var config = new VanillaGraphicsExpanded.LumOn.VgeConfig();
        config.LumOn.Enabled = lumon;
        config.WaterRefractionEnabled = false;
        // Act and assert: Repeated toggles invalidate publication, resize both images together and preserve borrowed color.
        Assert.Null(source.BeginFrame(config.WaterRefractionEnabled, composite));
        Assert.Null(source.Color);
        Assert.Null(source.Depth);
        for (int transition = 0; transition < 3; transition++)
        {
            config.WaterRefractionEnabled = true;
            var target = source.BeginFrame(config.WaterRefractionEnabled, composite)!;
            Assert.False(source.Published);
            Assert.Equal((GpuTextureRef)composite, target.Colors[0].Image!.Value.Texture);
            Assert.NotSame(composite, source.Color);
            Assert.NotEqual(composite.TextureId, source.Color!.TextureId);
            source.Color.UploadDataImmediate(Enumerable.Repeat(.5f, 16).ToArray());
            source.Depth!.UploadDataImmediate(Enumerable.Repeat(.75f, 4).ToArray());
            source.Publish();
            Assert.True(source.Published);
            composite.UploadDataImmediate(Enumerable.Repeat(2f, 16).ToArray());
            ImageAssertions.Comparisons.CompareConstant(source.Color, .5f).AssertEqual();
            // A new frame must withdraw last frame's publication before any consumer can use it.
            Assert.Same(target, source.BeginFrame(true, composite));
            Assert.False(source.Published);
            source.Publish();
            config.LumOn.Enabled = !config.LumOn.Enabled;
            Assert.Same(target, source.BeginFrame(true, composite));
            Assert.False(source.Published);
            var beforeResize = source.Color;
            // The owner keeps the attachment alive through pass end and resizes outside the boundary.
            var context = RHICommandContext.Current;
            using (var boundary = context.EnterBoundary(new("Tests.Water.ActiveSource", true,
                operations: BoundaryOperations.RenderPasses)))
                boundary.Run(() =>
                {
                    context.BeginRenderPass(target);
                    try
                    {
                        Assert.True(composite.IsValid);
                        Assert.Equal(2, composite.Width);
                        Assert.Equal(2, composite.Height);
                    }
                    finally { context.EndRenderPass(); }
                });
            composite.Resize(4, 2);
            source.BeginFrame(true, composite);
            Assert.False(beforeResize!.IsValid);
            Assert.Equal(4, source.Color!.Width);
            source.BeginFrame(true, expanded);
            Assert.False(beforeResize!.IsValid);
            Assert.Equal(4, source.Color!.Width);
            composite.Resize(2, 2);
            source.BeginFrame(true, composite);
            var oldColor = source.Color;
            var oldDepth = source.Depth;
            source.BeginFrame(true, resized);
            Assert.False(oldColor!.IsValid);
            Assert.False(oldDepth!.IsValid);
            Assert.Equal(3, source.Color!.Width);
            Assert.Equal(1, source.Depth!.Height);
            source.Publish();
            source.Invalidate();
            Assert.False(source.Published);
            config.WaterRefractionEnabled = false;
            Assert.Null(source.BeginFrame(config.WaterRefractionEnabled, resized));
            Assert.Null(source.Color);
            Assert.Null(source.Depth);
            Assert.True(composite.IsValid);
            Assert.True(resized.IsValid);
        }
        source.Dispose();
        Assert.False(source.Published);
        Assert.Equal(ErrorCode.NoError, GL.GetError());
    }
    #endregion
    #region Private
    /// <summary>Prepares this suite's declared observation programs before subject execution.</summary>
    private protected override void PrepareObservations(VanillaGraphicsExpanded.Tests.Support.GPU.Observations.GpuObservationPrograms programs)
    {
        base.PrepareObservations(programs);
        programs.Prepare("compare", 0, 0);
    }
    #endregion
}
