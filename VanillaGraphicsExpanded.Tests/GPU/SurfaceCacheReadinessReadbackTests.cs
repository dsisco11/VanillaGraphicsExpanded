using System.Diagnostics;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Tests.GPU.Fixtures;

namespace VanillaGraphicsExpanded.Tests.GPU;

/// <summary>Checks exact requested coverage and fresh observations in the readiness readback helper.</summary>
[Collection("GPU")]
[Trait("Category", "GPU")]
public sealed class SurfaceCacheReadinessReadbackTests(HeadlessGLFixture fixture, ITestOutputHelper output) : RenderTestBase(fixture)
{
    #region Coverage and freshness
    /// <summary>Adjacent, separated, row-edge and layer-edge requests ignore poisoned unrequested texels.</summary>
    [Fact]
    public void RequestedRegionsIgnoreUnrequestedPoison()
    {
        EnsureContextValid();
        uint[] pages = [1, 2, 4, 5, 6, 16, 17, 18, 21, 22];
        using var texture = CreateAtlas(pages);
        var regions = SurfaceCacheReadinessReadback.Plan(pages, 2, 4, 16);
        Assert.Equal(6, regions.Length);
        Assert.True(SurfaceCacheReadinessReadback.AllInitialized(texture, regions));
        Assert.Equal(ErrorCode.NoError, GL.GetError());
    }

    /// <summary>Every requested texel matters, and repeated calls see content and object replacement immediately.</summary>
    [Theory]
    [InlineData(0f)]
    [InlineData(float.NaN)]
    public void LastTexelAndReplacementAreObservedFresh(float missingAlpha)
    {
        EnsureContextValid();
        uint[] pages = [1, 2, 17, 18];
        var regions = SurfaceCacheReadinessReadback.Plan(pages, 2, 4, 16);
        using var texture = CreateAtlas(pages);
        Assert.True(SurfaceCacheReadinessReadback.AllInitialized(texture, regions));
        // Last texel of the last requested page: no frame advance or new plan may mask a stale observation.
        texture.UploadDataImmediate(new float[] { 0, 0, 0, missingAlpha }, 3, 1, 1, 1, 1, 1);
        Assert.False(SurfaceCacheReadinessReadback.AllInitialized(texture, regions));
        using var replacement = CreateAtlas(pages);
        Assert.True(SurfaceCacheReadinessReadback.AllInitialized(replacement, regions));
        Assert.False(SurfaceCacheReadinessReadback.AllInitialized(texture, regions));
        texture.UploadDataImmediate(new float[] { 0, 0, 0, 1 }, 3, 1, 1, 1, 1, 1);
        Assert.True(SurfaceCacheReadinessReadback.AllInitialized(texture, regions));
        Assert.Equal(ErrorCode.NoError, GL.GetError());
    }
    #endregion

    #region Matched observation work
    /// <summary>Records per-page and merged-read costs for identical initialized tiles without a timing assertion.</summary>
    [Fact]
    public void MatchedRequestedPagesRetainCoverageWithFewerReads()
    {
        EnsureContextValid();
        uint[] pages = Enumerable.Range(1, 24).Select(value => (uint)value).ToArray();
        using var texture = CreateAtlas(pages);
        var regions = SurfaceCacheReadinessReadback.Plan(pages, 2, 4, 16);
        Assert.Equal(6, regions.Length);
        Assert.Equal(24 << 2, regions.Sum(region => region.Width * region.Height));
        Assert.True(ReadPerPage(texture, pages));
        Assert.True(SurfaceCacheReadinessReadback.AllInitialized(texture, regions));
        double scalarMs = 0, mergedMs = 0;
        // Alternate order to avoid assigning every first observation to the same implementation.
        for (int iteration = 0; iteration < 8; iteration++)
        {
            for (int position = 0; position < 2; position++)
            {
                bool scalar = ((iteration + position) & 1) == 0;
                long started = Stopwatch.GetTimestamp();
                bool ready = scalar ? ReadPerPage(texture, pages) : SurfaceCacheReadinessReadback.AllInitialized(texture,
                    SurfaceCacheReadinessReadback.Plan(pages, 2, 4, 16));
                double elapsed = Stopwatch.GetElapsedTime(started).TotalMilliseconds;
                Assert.True(ready);
                if (scalar) scalarMs += elapsed; else mergedMs += elapsed;
            }
        }
        output.WriteLine($"8 observations each; per-page reads={24 << 3}, merged reads={6 << 3}; texels per observation={24 << 2}; scalar ms={scalarMs:F6}; merged ms={mergedMs:F6}.");
        Assert.Equal(ErrorCode.NoError, GL.GetError());
    }
    #endregion

    #region Authored atlas and reference observation
    /// <summary>Initializes requested pages and poisons every other alpha, across two atlas layers.</summary>
    private static Texture3D CreateAtlas(uint[] pages)
    {
        var texture = Texture3D.Create(8, 8, 2, PixelInternalFormat.Rgba32f, TextureFilterMode.Nearest, TextureTarget.Texture2DArray);
        float[] data = new float[128 << 2];
        for (int i = 3; i < data.Length; i += 4) data[i] = float.NaN;
        foreach (uint page in pages)
        {
            int physical = (int)page - 1, layer = physical >> 4, local = physical & 15;
            for (int y = 0; y < 2; y++) for (int x = 0; x < 2; x++)
            {
                int row = (layer << 3) + ((local >> 2) << 1) + y;
                int column = ((local & 3) << 1) + x;
                data[(((row << 3) + column) << 2) + 3] = 1;
            }
        }
        texture.UploadDataImmediate(data, 0, 0, 0, 8, 8, 2);
        return texture;
    }

    /// <summary>Reproduces the previous one-framebuffer, one-read-per-page observation for matched work.</summary>
    private static bool ReadPerPage(Texture3D texture, uint[] pages)
    {
        using var framebuffer = GpuFramebuffer.CreateEmpty("Tests.Readiness.PerPage");
        framebuffer.Bind();
        try
        {
            float[] pixels = new float[16];
            foreach (uint page in pages)
            {
                int physical = (int)page - 1, local = physical & 15;
                GL.FramebufferTextureLayer(FramebufferTarget.Framebuffer, FramebufferAttachment.ColorAttachment0, texture.TextureId, 0, physical >> 4);
                GL.ReadBuffer(ReadBufferMode.ColorAttachment0);
                GL.ReadPixels(((local & 3) << 1), ((local >> 2) << 1), 2, 2, PixelFormat.Rgba, PixelType.Float, pixels);
                if (!SurfaceCacheReadinessReadback.IsFullyInitialized(pixels)) return false;
            }
            return true;
        }
        finally { GpuFramebuffer.Unbind(); }
    }
    #endregion
}
