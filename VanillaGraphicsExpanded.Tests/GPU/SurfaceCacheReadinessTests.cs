using VanillaGraphicsExpanded.Rendering.RHI;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Tests.GPU.Fixtures;

namespace VanillaGraphicsExpanded.Tests.GPU;

/// <summary>Checks exact requested coverage and fresh observations in the readiness readback helper.</summary>
[Collection("GPU")]
[Trait("Category", "GpuIntegration")]
public sealed class SurfaceCacheReadinessTests(HeadlessGLFixture fixture, ITestOutputHelper output) : RenderTestBase(fixture)
{
    #region Coverage and freshness
    /// <summary>Adjacent, separated, row-edge and layer-edge requests ignore poisoned unrequested texels.</summary>
    [Fact]
    public void RequestedRegionsIgnoreUnrequestedPoison()
    {
        EnsureContextValid();
        uint[] pages = [1, 2, 4, 5, 6, 16, 17, 18, 21, 22];
        using var texture = CreateAtlas(pages);
        var regions = SurfaceCacheReadiness.Plan(pages, 2, 4, 16);
        Assert.Equal(6, regions.Length);
        Assert.False(SurfaceCacheReadiness.AllInitialized(texture, [], ImageAssertions));
        Assert.True(SurfaceCacheReadiness.AllInitialized(texture, regions, ImageAssertions));
        Assert.Equal(ErrorCode.NoError, GL.GetError());
    }

    /// <summary>Every requested texel matters, and repeated calls see content and object replacement immediately.</summary>
    [Theory]
    [InlineData(0, 0f)] [InlineData(5, 0f)] [InlineData(15, 0f)]
    [InlineData(0, float.NaN)] [InlineData(5, float.NaN)] [InlineData(15, float.NaN)]
    public void RequestedTexelsAndReplacementAreObservedFresh(int texel, float missingAlpha)
    {
        EnsureContextValid();
        uint[] pages = [1, 2, 17, 18];
        var regions = SurfaceCacheReadiness.Plan(pages, 2, 4, 16);
        using var texture = CreateAtlas(pages);
        Assert.True(SurfaceCacheReadiness.AllInitialized(texture, regions, ImageAssertions));
        // First, interior and final requested texels must remain observable without a frame advance or new plan.
        int x = texel % 4, y = texel / 4 % 2, layer = texel / 8;
        texture.UploadDataImmediate(new float[] { 0, 0, 0, missingAlpha }, x, y, layer, 1, 1, 1);
        Assert.False(SurfaceCacheReadiness.AllInitialized(texture, regions, ImageAssertions));
        using var replacement = CreateAtlas(pages);
        Assert.True(SurfaceCacheReadiness.AllInitialized(replacement, regions, ImageAssertions));
        Assert.False(SurfaceCacheReadiness.AllInitialized(texture, regions, ImageAssertions));
        texture.UploadDataImmediate(new float[] { 0, 0, 0, 1 }, x, y, layer, 1, 1, 1);
        Assert.True(SurfaceCacheReadiness.AllInitialized(texture, regions, ImageAssertions));
        Assert.Equal(ErrorCode.NoError, GL.GetError());
    }
    #endregion

    #region Matched observation work
    /// <summary>Checks merged coverage against every authored page tail without a timing workload.</summary>
    [Fact]
    public void MergedRequestedPagesRetainEveryPageTail()
    {
        EnsureContextValid();
        uint[] pages = Enumerable.Range(1, 24).Select(value => (uint)value).ToArray();
        using var texture = CreateAtlas(pages);
        var regions = SurfaceCacheReadiness.Plan(pages, 2, 4, 16);
        Assert.Equal(6, regions.Length);
        Assert.False(SurfaceCacheReadiness.AllInitialized(texture, [], ImageAssertions));
        Assert.Equal(24 << 2, regions.Sum(region => region.Width * region.Height));
        Assert.True(SurfaceCacheReadiness.AllInitialized(texture, regions, ImageAssertions));
        // Retain the exact 24-page coverage: changing each requested tail must be observed independently.
        foreach (uint page in pages)
        {
            int physical = (int)page - 1, local = physical & 15;
            int x = ((local & 3) << 1) + 1, y = ((local >> 2) << 1) + 1, layer = physical >> 4;
            texture.UploadDataImmediate(new float[] { 0, 0, 0, 0 }, x, y, layer, 1, 1, 1);
            Assert.False(SurfaceCacheReadiness.AllInitialized(texture, regions, ImageAssertions));
            texture.UploadDataImmediate(new float[] { 0, 0, 0, 1 }, x, y, layer, 1, 1, 1);
        }
        Assert.Equal(ErrorCode.NoError, GL.GetError());
    }
    #endregion

    #region Authored atlas and reference observation
    /// <summary>Initializes requested pages and poisons every other alpha, across two atlas layers.</summary>
    private static Texture3D CreateAtlas(uint[] pages)
    {
        var texture = RHICommandContext.Current.CreateTexture3D(new RHITextureDescription { Dimension = TextureDimension.TwoArray, Format = TextureFormat.Rgba32f, Width = System.Math.Max(1, 8), Height = System.Math.Max(1, 8), Depth = System.Math.Max(1, 2), MipLevels = 1, Storage = ResourceStorageMode.Mutable, Filter = TextureFilterMode.Nearest, BaseLevel = 0, MaxLevel = (1) - 1, Label = null }).RequireResource();
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

    #endregion
}
