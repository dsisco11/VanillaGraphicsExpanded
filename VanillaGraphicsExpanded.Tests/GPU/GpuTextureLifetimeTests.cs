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
        Assert.Equal(0, StateCache.Current.GetBoundTexture(TextureTarget.Texture2D, 2));
        Assert.Equal(target.TextureId, StateCache.Current.GetBoundTexture(TextureTarget.Texture2D, 3));
        StateCache.Current.ActiveTexture(0);
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
            Assert.Equal(retiredId, StateCache.Current.GetBoundTexture(TextureTarget.Texture2D, 0));
            manager.OnRenderFrame(0, GpuResourceManager.Stage);
            Assert.False(GL.IsTexture(retiredId));
            Assert.Equal(0, StateCache.Current.GetBoundTexture(TextureTarget.Texture2D, 0));
            target.UploadDataImmediate(new float[] { 0, 0, 0, 1 });
            Assert.Equal(ErrorCode.NoError, GL.GetError());
        }
        finally
        {
            GpuResourceManagerSystem.Shutdown();
            TextureStreamingSystem.Dispose();
        }
    }
    /// <summary>Retiring a typed borrower withdraws its handle without deleting or mutating the external image.</summary>
    [Fact]
    public void BorrowedTextureRetirementPreservesExternalStorage()
    {
        EnsureContextValid();
        using var external = DynamicTexture2D.CreateWithData(2,1,PixelInternalFormat.Rgba32f,[8f,4f,2f,.3f,1f,2f,3f,.7f]);
        external.Bind(3);
        int handle = external.TextureId;
        using var borrowed = new BorrowedTexture(handle);
        Assert.Equal(handle,borrowed.TextureId);
        Assert.Equal((2,1),(borrowed.Width,borrowed.Height));
        Assert.True(borrowed.IsValid);
        Assert.Throws<NotSupportedException>(()=>borrowed.Detach());
        borrowed.Dispose();
        Assert.False(borrowed.IsValid);
        Assert.Equal(0,borrowed.TextureId);
        Assert.True(GL.IsTexture(handle));
        Assert.Equal(handle,StateCache.Current.GetBoundTexture(TextureTarget.Texture2D,3));
        Assert.Equal(new[]{8f,4f,2f,.3f,1f,2f,3f,.7f},external.ReadPixels());
        Assert.Equal(ErrorCode.NoError,GL.GetError());
        external.Dispose();
        Assert.False(GL.IsTexture(handle));
    }
    /// <summary>Borrowing preserves native target, spatial dimensions, array extent and allocated mip metadata.</summary>
    [Theory]
    [InlineData(TextureTarget.Texture1D,8,1,1)]
    [InlineData(TextureTarget.Texture3D,8,4,4)]
    [InlineData(TextureTarget.Texture2DArray,8,4,7)]
    public void BorrowedTextureRetainsNonTwoDimensionalStorage(TextureTarget target,int width,int height,int depth)
    {
        EnsureContextValid();
        GL.CreateTextures(target,1,out int texture);
        try
        {
            if(target==TextureTarget.Texture1D) GL.TextureStorage1D(texture,3,SizedInternalFormat.Rgba16f,width);
            else GL.TextureStorage3D(texture,3,SizedInternalFormat.Rgba16f,width,height,depth);
            using(var borrowed=new BorrowedTexture(texture))
            {
                Assert.Equal(target,borrowed.TextureTarget);
                Assert.Equal((width,height,depth),(borrowed.Width,borrowed.Height,borrowed.Depth));
                Assert.Equal(PixelInternalFormat.Rgba16f,borrowed.InternalFormat);
                Assert.Equal(3,borrowed.StorageMipLevels);
            }
            Assert.True(GL.IsTexture(texture));
            Assert.Equal(ErrorCode.NoError,GL.GetError());
        }
        finally { StateCache.Current.DeleteTexture(texture); }
    }
    #endregion
}
