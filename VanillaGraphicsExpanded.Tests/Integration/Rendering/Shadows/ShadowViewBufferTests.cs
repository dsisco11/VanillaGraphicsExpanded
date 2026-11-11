using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.Numerics;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Rendering.RHI;
using VanillaGraphicsExpanded.Rendering.Shadows.Configuration;
using VanillaGraphicsExpanded.Rendering.Shadows.Geometry;
using VanillaGraphicsExpanded.Rendering.Shadows.Lights;
using VanillaGraphicsExpanded.Rendering.Shadows.Pages;
using VanillaGraphicsExpanded.Rendering.Shadows.Projections;
using VanillaGraphicsExpanded.Tests.Support.GPU.Harness;
using VanillaGraphicsExpanded.Tests.Support.GPU.Shaders;
using Xunit;

namespace VanillaGraphicsExpanded.Tests.Integration.Rendering.Shadows;

/// <summary>Protects bounded compact view records, virtual raster areas and their charged GPU lifetime.</summary>
[Collection("GPU")]
[Trait("Category", "GpuIntegration")]
public sealed class ShadowViewBufferTests(HeadlessGLFixture fixture) : RenderTestBase(fixture)
{
    #region Public API
    /// <summary>View-count and aggregate raster-pixel limits reject layouts before publishing record storage.</summary>
    [Fact]
    public void ViewBufferEnforcesConfigurableViewAndRasterBudgets()
    {
        // Arrange: create two virtual views whose combined raster area is 96 pixels.
        EnsureContextValid(); fixture.InitializeResourceDisposal();
        using var assets = new BinaryShaderApiFixture();
        using var pool = Pool(assets.Api);
        Assert.True(pool.BeginBatch());
        var views = Views();

        // Act and assert: view count and aggregate area each reject only when their own configured limit is exceeded.
        Assert.Throws<InvalidOperationException>(() => new ShadowViewBuffer(views, pool,
            new ShadowDrawSettings { MemoryMiB = 1, ViewCapacity = 1, RasterPixels = 128 }));
        Assert.Throws<InvalidOperationException>(() => new ShadowViewBuffer(views, pool,
            new ShadowDrawSettings { MemoryMiB = 1, ViewCapacity = 2, RasterPixels = 95 }));
        using var admitted = new ShadowViewBuffer(views, pool,
            new ShadowDrawSettings { MemoryMiB = 1, ViewCapacity = 2, RasterPixels = 96 });
        Assert.Equal(2, admitted.Count);
        pool.SealBatch();
    }

    /// <summary>View records expose origin-zero pass areas and remain charged until their submitted users retire.</summary>
    [Fact]
    public void ViewBufferDescribesPerViewRasterAreaAndRetiresAfterFence()
    {
        // Arrange: admit two views with different extents while charging their shared maximum-size R8 target.
        EnsureContextValid(); fixture.InitializeResourceDisposal();
        using var assets = new BinaryShaderApiFixture();
        using var pool = Pool(assets.Api);
        Assert.True(pool.BeginBatch());
        using var views = new ShadowViewBuffer(Views(), pool,
            new ShadowDrawSettings { MemoryMiB = 1, ViewCapacity = 2, RasterPixels = 128 });

        // Act: capture the intended raster pass, then submit the view record's retirement fence.
        var firstPass = views.DescribePass(0);
        var secondPass = views.DescribePass(1);
        long charged = views.AllocatedBytes;
        views.Seal();
        GL.Finish();
        bool retired = views.Poll();

        // Assert: records select their own raster sizes and accounting drops only after fence completion.
        Assert.Equal(2, views.Count);
        Assert.Equal(new RenderArea(0, 0, 8, 8), firstPass.Area!.Value);
        Assert.Equal(new RenderArea(0, 0, 4, 8), secondPass.Area!.Value);
        Assert.Equal(576L, charged);
        Assert.True(retired);
        Assert.Equal(0, views.AllocatedBytes);
        Assert.False(views.Texture.IsValid);
        pool.SealBatch();
    }
    #endregion

    #region Private
    /// <summary>Creates an open one-bank page pool used only to validate the immutable view-record contract.</summary>
    private static ShadowPagePool Pool(Vintagestory.API.Common.ICoreAPI api) =>
        ShadowPagePool.Create(api, new ShadowPageSettings
        {
            MemoryMiB = 4, PageSize = 8, TileColumns = 1, TileRows = 1, BankCapacity = 1
        });

    /// <summary>Builds two views with distinct virtual pass areas but the same finite directional projection.</summary>
    private static ShadowDrawView[] Views()
    {
        var source = new ShadowSourceKey(83, 0, ShadowSourceKind.Sun, 5, 17, 0);
        var projection = new DirectionalShadowProjection(source, 1, Vector3d.UnitZ,
            new ShadowBounds(-16, -16, -16, 16, 16, 16), baseTexelWidth: 1, levels: 1, pageSize: 8);
        return
        [
            ShadowDrawView.Directional(projection, 0, new(0, 0, 8, 8), new(2, 2, 4, 4), Vector3d.Zero),
            ShadowDrawView.Directional(projection, 0, new(0, 0, 4, 8), new(0, 2, 4, 4), Vector3d.Zero)
        ];
    }
    #endregion
}
