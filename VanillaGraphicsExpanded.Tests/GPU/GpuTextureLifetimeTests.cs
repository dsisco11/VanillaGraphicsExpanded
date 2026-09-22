using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Tests.GPU.Fixtures;

namespace VanillaGraphicsExpanded.Tests.GPU;

/// <summary>Checks that texture lifetime changes cannot leave deleted objects in cached binding restoration.</summary>
[Collection("GPU")]
[Trait("Category", "GPU")]
public sealed class GpuTextureLifetimeTests : RenderTestBase
{
    /// <summary>Uses the shared GL context.</summary>
    public GpuTextureLifetimeTests(HeadlessGLFixture fixture) : base(fixture) { }

    #region Deletion and upload
    /// <summary>A later upload scope restores zero after the texture formerly bound on its unit is deleted.</summary>
    [Fact]
    public void DeletedBoundTextureIsNotRestoredByLaterUpload()
    {
        EnsureContextValid();
        using var target = DynamicTexture2D.Create(1, 1, PixelInternalFormat.Rgba32f);
        using var retired = DynamicTexture2D.Create(1, 1, PixelInternalFormat.Rgba32f);
        target.Bind(3);
        retired.Bind(0);
        retired.Bind(2);
        int retiredId = retired.TextureId;
        retired.Dispose();
        Assert.False(GL.IsTexture(retiredId));
        Assert.Equal(ErrorCode.NoError, GL.GetError());
        target.UploadDataImmediate(new float[] { .25f, .5f, .75f, 1 });
        Assert.Equal(ErrorCode.NoError, GL.GetError());
        Assert.Equal(0, GlStateCache.Current.GetBoundTexture(TextureTarget.Texture2D, 2));
        Assert.Equal(target.TextureId, GlStateCache.Current.GetBoundTexture(TextureTarget.Texture2D, 3));
        GlStateCache.Current.ActiveTexture(0);
        Assert.Equal(0, GL.GetInteger(GetPName.TextureBinding2D));
        Assert.Equal(new float[] { .25f, .5f, .75f, 1 }, target.ReadPixels());
        Assert.Equal(ErrorCode.NoError, GL.GetError());
    }
    /// <summary>Background disposal preserves bindings until the render-thread queue actually deletes the texture.</summary>
    [Fact]
    public void QueuedDeletionUpdatesCacheOnlyWhenDrained()
    {
        EnsureContextValid();
        using var manager = new GpuResourceManager();
        GpuResourceManagerSystem.Initialize(manager);
        try
        {
            manager.OnRenderFrame(0, GpuResourceManager.Stage);
            using var target = DynamicTexture2D.Create(1, 1, PixelInternalFormat.Rgba32f);
            using var retired = DynamicTexture2D.Create(1, 1, PixelInternalFormat.Rgba32f);
            retired.Bind(0);
            int retiredId = retired.TextureId;
            Exception? workerError = null;
            var worker = new Thread(() => { try { retired.Dispose(); } catch (Exception error) { workerError = error; } }) { IsBackground = true };
            worker.Start(); worker.Join();
            Assert.Null(workerError);
            Assert.True(GL.IsTexture(retiredId));
            Assert.Equal(retiredId, GlStateCache.Current.GetBoundTexture(TextureTarget.Texture2D, 0));
            manager.OnRenderFrame(0, GpuResourceManager.Stage);
            Assert.False(GL.IsTexture(retiredId));
            Assert.Equal(0, GlStateCache.Current.GetBoundTexture(TextureTarget.Texture2D, 0));
            target.UploadDataImmediate(new float[] { 0, 0, 0, 1 });
            Assert.Equal(ErrorCode.NoError, GL.GetError());
        }
        finally
        {
            GpuResourceManagerSystem.Shutdown();
            TextureStreamingSystem.Dispose();
        }
    }
    #endregion
}
