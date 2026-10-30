using VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;
using VanillaGraphicsExpanded.Rendering.RHI;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.PBR.Liquids;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Tests.GPU.Fixtures;

namespace VanillaGraphicsExpanded.Tests.GPU;

/// <summary>Checks immutable source lifetime independently of the selected lighting implementation.</summary>
[Collection("GPU")]
[Trait("Category", "GPU")]
public sealed class WaterRefractionLifecycleTests(HeadlessGLFixture fixture) : RenderTestBase(fixture)
{
    #region Public API
    /// <summary>Capture owns only its receiver pair and retains sparse output routing across reuse and resize.</summary>
    [Fact]
    public void CaptureTargetOwnsOnlyReceiverAttachments()
    {
        EnsureContextValid();
        using var bindings = OpenGlStateCache.Current.BindFramebufferScope();
        using var source = new WaterRefractionScene();
        var target = source.BeginCapture(2, 2)!;
        Assert.IsType<RHIRenderPassDescription>(target);
        Assert.True(target.Colors[0].DiscardOutput);
        Assert.Equal((GpuTextureRef)source.Color!, target.Colors[1].Image!.Value.Texture);
        Assert.Equal((GpuTextureRef)source.Depth!, target.Colors[2].Image!.Value.Texture);
        Assert.Equal(3, target.Colors.Length);
        var context=RHICommandContext.Current;
        using (var boundary=context.EnterBoundary(new("Tests.Water.CaptureTargets",true,operations:BoundaryOperations.RenderPasses)))
            boundary.Run(()=>{ context.BeginRenderPass(target); context.EndRenderPass(); });
        source.Publish();
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
        EnsureContextValid();
        using var source = new WaterRefractionScene();
        using var composite = RHICommandContext.Current.CreateDynamicTexture2D(new RHITextureDescription { Dimension = TextureDimension.Two, Format = TextureFormat.Rgba16f, Width = System.Math.Max(1, 2), Height = System.Math.Max(1, 2), MipLevels = 1, Storage = ResourceStorageMode.Mutable, Filter = TextureFilterMode.Nearest, BaseLevel = 0, MaxLevel = (1) - 1, Label = null }).RequireResource();
        using var resized = RHICommandContext.Current.CreateDynamicTexture2D(new RHITextureDescription { Dimension = TextureDimension.Two, Format = TextureFormat.Rgba16f, Width = System.Math.Max(1, 3), Height = System.Math.Max(1, 1), MipLevels = 1, Storage = ResourceStorageMode.Mutable, Filter = TextureFilterMode.Nearest, BaseLevel = 0, MaxLevel = (1) - 1, Label = null }).RequireResource();
        using var expanded = RHICommandContext.Current.CreateDynamicTexture2D(new RHITextureDescription { Dimension = TextureDimension.Two, Format = TextureFormat.Rgba16f, Width = System.Math.Max(1, 4), Height = System.Math.Max(1, 2), MipLevels = 1, Storage = ResourceStorageMode.Mutable, Filter = TextureFilterMode.Nearest, BaseLevel = 0, MaxLevel = (1) - 1, Label = null }).RequireResource();
        var config = new VanillaGraphicsExpanded.LumOn.VgeConfig();
        config.LumOn.Enabled = lumon;
        config.WaterRefractionEnabled = false;
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
            Assert.All(source.Color.ReadPixels(), value => Assert.Equal(.5f, value));
            // A new frame must withdraw last frame's publication before any consumer can use it.
            Assert.Same(target, source.BeginFrame(true, composite));
            Assert.False(source.Published);
            source.Publish();
            config.LumOn.Enabled = !config.LumOn.Enabled;
            Assert.Same(target, source.BeginFrame(true, composite));
            Assert.False(source.Published);
            var beforeResize = source.Color;
            // An active pass protects its image; retaining plain intentions does not lock storage between frames.
            var context = RHICommandContext.Current;
            using (var boundary = context.EnterBoundary(new("Tests.Water.ActiveSource", true,
                operations: BoundaryOperations.RenderPasses)))
                boundary.Run(() =>
                {
                    context.BeginRenderPass(target);
                    try { Assert.Throws<InvalidOperationException>(() => composite.Resize(4, 2)); }
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
}
