using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.LumOn;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Rendering.Pipeline;
using VanillaGraphicsExpanded.Rendering.Pipeline.Passes;
using VanillaGraphicsExpanded.Tests.GPU.Fixtures;

namespace VanillaGraphicsExpanded.Tests.GPU;

/// <summary>Checks production history clears and shader-free pass submission against hostile engine state.</summary>
[Collection("GPU")]
[Trait("Category", "GPU")]
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
                buffers.ProbeSh9Tex0!, buffers.ProbeSh9Tex1!, buffers.ProbeSh9Tex2!, buffers.ProbeSh9Tex3!,
                buffers.ProbeSh9Tex4!, buffers.ProbeSh9Tex5!, buffers.ProbeSh9Tex6!,
                buffers.IndirectHalfTex!, buffers.IndirectFullTex!, buffers.ProbeTraceMaskTex!, buffers.ProbePisEnergyTex!, buffers.VelocityTex!];
            foreach (var image in images)
                image.UploadDataImmediate(Enumerable.Repeat(.75f, image.ReadPixels().Length).ToArray());
            buffers.HasPublishedIndirect = true;
            var revision = buffers.HistoryRevision;
            long draws = StateCache.Current.DrawSubmissions;
            using (var hostile = new HostileFullscreenState())
            {
                buffers.ClearHistory();
                hostile.AssertRestored();
                Assert.All(images, image => Assert.All(image.ReadPixels(), value => Assert.Equal(0, value)));
            }
            Assert.False(buffers.HasPublishedIndirect);
            Assert.True(buffers.HistoryRevision > revision);
            Assert.Equal(draws, StateCache.Current.DrawSubmissions);
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
        using var target = CreateRenderTarget(4, 4, PixelInternalFormat.Rgba32f);
        long draws = StateCache.Current.DrawSubmissions;
        using var hostile = new HostileFullscreenState();
        var expected = new ArithmeticException("after clear");
        bool Run() => GraphicsCommandContext.TryRun("ClearOnlyFixture", [], true, commands =>
        {
            commands.BeginPass(new(target, [new(0, AttachmentLoad.Clear, Clear: ColorClearValue.Float(.25f, .5f, .75f, 1))]));
            if (fail) throw expected;
            commands.EndPass();
        });
        if (fail) Assert.Same(expected, Assert.Throws<ArithmeticException>(() => Run())); else Assert.True(Run());
        hostile.AssertRestored();
        float[] pixels = target[0].ReadPixels();
        for (int i = 0; i < pixels.Length; i++) Assert.Equal(new[] { .25f, .5f, .75f, 1 }[i % 4], pixels[i]);
        Assert.Equal(draws, StateCache.Current.DrawSubmissions);
        Assert.Equal(ErrorCode.NoError, GL.GetError());
    }
    #endregion
}
