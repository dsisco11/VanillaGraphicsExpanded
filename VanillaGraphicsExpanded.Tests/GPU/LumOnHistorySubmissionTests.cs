using VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;
using VanillaGraphicsExpanded.Rendering.RHI;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.LumOn;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Rendering.Pipeline;
using VanillaGraphicsExpanded.Rendering.Pipeline.Passes;
using VanillaGraphicsExpanded.Tests.GPU.Fixtures;

namespace VanillaGraphicsExpanded.Tests.GPU;

/// <summary>Checks production history clears and shader-free pass submission against hostile engine state.</summary>
[Collection("GPU")]
[Trait("Category", "GpuIntegration")]
public sealed class LumOnHistorySubmissionTests(HeadlessGLFixture fixture) : RenderTestBase(fixture)
{
    #region Public API
    /// <summary>The actual buffer owner clears all temporal roles and restores the caller without issuing draws.</summary>
    [Fact]
    public void ClearHistoryClearsPublishedImagesAcrossResize()
    {
        EnsureContextValid();
        using var assets = new BinaryShaderApiFixture();
        var config = new VgeConfig(); config.LumOn.ProbeSpacingPx = 8;
        using var buffers = new LumOnBufferManager(assets.Api, config);
        foreach (int size in new[] { 16, 24 })
        {
            buffers.EnsureBuffers(size, size);
            Assert.True(buffers.EnsureBuffers(size, size));
            DynamicTexture2D[] images = [buffers.ScreenProbeAtlasTraceTex!, buffers.ScreenProbeAtlasMetaTraceTex!,
                buffers.ScreenProbeAtlasCurrentTex!, buffers.ScreenProbeAtlasMetaCurrentTex!,
                buffers.ScreenProbeAtlasHistoryTex!, buffers.ScreenProbeAtlasMetaHistoryTex!,
                buffers.ScreenProbeAtlasFilteredTex!, buffers.ScreenProbeAtlasMetaFilteredTex!,
                buffers.IndirectHalfTex!, buffers.IndirectFullTex!, buffers.ProbeTraceMaskTex!, buffers.ProbePisEnergyTex!, buffers.VelocityTex!];
            foreach (var image in images)
            {
                int channels=RHICommandContext.Current.GetTextureEncoding(image,PixelComponentType.Float).Channels switch
                {
                    PixelChannelLayout.Red or PixelChannelLayout.RedInteger=>1,
                    PixelChannelLayout.Rg or PixelChannelLayout.RgInteger=>2,
                    PixelChannelLayout.Rgb or PixelChannelLayout.RgbInteger=>3,
                    PixelChannelLayout.Rgba or PixelChannelLayout.RgbaInteger=>4,
                    _=>throw new InvalidOperationException("Expected a color history image.")
                };
                image.UploadDataImmediate(Enumerable.Repeat(.75f,image.Width*image.Height*channels).ToArray());
            }
            var coefficients = buffers.ProbeSh9!;
            coefficients.UploadDataImmediate(Enumerable.Repeat(.75f, coefficients.Width * coefficients.Height * 28).ToArray(), 0, 0, 0, coefficients.Width, coefficients.Height, 7);
            buffers.HasPublishedIndirect = true;
            var revision = buffers.HistoryRevision;
#if DEBUG
            long draws = OpenGlStateCache.Current.DrawSubmissions;
#endif
            using (var hostile = new HostileFullscreenState())
            {
                buffers.ClearHistory();
                hostile.AssertRestored();
                Assert.All(images, image => ImageAssertions.CompareConstant(image, 0).AssertEqual());
                ImageAssertions.CompareConstant(coefficients,0).AssertEqual();
            }
            Assert.False(buffers.HasPublishedIndirect);
            Assert.True(buffers.HistoryRevision > revision);
#if DEBUG
            Assert.Equal(draws, OpenGlStateCache.Current.DrawSubmissions);
#endif
        }
        Assert.Equal(ErrorCode.NoError, GL.GetError());
    }

    /// <summary>An empty pipeline declaration permits clear-only passes and retains exception-safe restoration.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ClearOnlyBoundaryRestoresOnSuccessAndException(bool fail)
    {
        EnsureContextValid();
        using var target = CreateRenderTarget(4, 4, TextureFormat.Rgba32f);
#if DEBUG
        long draws = OpenGlStateCache.Current.DrawSubmissions;
#endif
        using var hostile = new HostileFullscreenState();
        var expected = new ArithmeticException("after clear");
        bool Run()
        {
            var commands=RHICommandContext.Current;
            using var boundary=commands.EnterBoundary(new("ClearOnlyFixture",true,operations:BoundaryOperations.RenderPasses));
            boundary.Run(()=>
            {
            commands.BeginRenderPass(FramebufferTestSetup.DescribeAttachments(target, [new(AttachmentIndex:0, Load:AttachmentLoad.Clear, Clear: ColorClearValue.Float(.25f, .5f, .75f, 1))]));
            if (fail) throw expected;
            commands.EndRenderPass();
            });
            return true;
        }
        if (fail) Assert.Same(expected, Assert.Throws<ArithmeticException>(() => Run())); else Assert.True(Run());
        hostile.AssertRestored();
        ImageAssertions.Compare(target[0],Enumerable.Repeat(new[]{.25f,.5f,.75f,1f},16).SelectMany(x=>x).ToArray()).AssertEqual();
#if DEBUG
        Assert.Equal(draws, OpenGlStateCache.Current.DrawSubmissions);
#endif
        Assert.Equal(ErrorCode.NoError, GL.GetError());
    }
    #endregion
}
