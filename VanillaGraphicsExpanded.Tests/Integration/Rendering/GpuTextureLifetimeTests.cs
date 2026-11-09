using VanillaGraphicsExpanded.Rendering.Integration.OpenGL;
using VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;
using System;
using VanillaGraphicsExpanded.Rendering.RHI;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.Rendering;

namespace VanillaGraphicsExpanded.Tests.Integration.Rendering;

/// <summary>Checks that texture lifetime changes cannot leave deleted objects in cached binding restoration.</summary>
[Collection("GPU")]
[Trait("Category", "GpuIntegration")]
public sealed class GpuTextureLifetimeTests : RenderTestBase
{
    #region Public API

    /// <summary>Uses the shared GL context.</summary>
    public GpuTextureLifetimeTests(HeadlessGLFixture fixture) : base(fixture) { }

    #region Deletion and upload
    /// <summary>A later upload scope restores zero after the texture formerly bound on its unit is deleted.</summary>
    [Fact]
    public void DeletedBoundTextureIsNotRestoredByLaterUpload()
    {
        // Arrange: Bind two live images to distinct units so retirement can be checked against surviving state.
        EnsureContextValid();
        using var target = RHICommandContext.Current.CreateDynamicTexture2D(new RHITextureDescription { Dimension = TextureDimension.Two, Format = TextureFormat.Rgba32f, Width = System.Math.Max(1, 1), Height = System.Math.Max(1, 1), MipLevels = 1, Storage = ResourceStorageMode.Mutable, Filter = TextureFilterMode.Nearest, BaseLevel = 0, MaxLevel = (1) - 1, Label = null }).RequireResource();
        using var retired = RHICommandContext.Current.CreateDynamicTexture2D(new RHITextureDescription { Dimension = TextureDimension.Two, Format = TextureFormat.Rgba32f, Width = System.Math.Max(1, 1), Height = System.Math.Max(1, 1), MipLevels = 1, Storage = ResourceStorageMode.Mutable, Filter = TextureFilterMode.Nearest, BaseLevel = 0, MaxLevel = (1) - 1, Label = null }).RequireResource();
        VanillaGraphicsExpanded.Rendering.RHI.RHICommandContext.Current.BindSampledTextures([new(3, target, Dimension: VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL.OpenGLImageValues.Dimension(OpenGLImageValues.Target(((GpuTextureRef)target).Target)))]);
        VanillaGraphicsExpanded.Rendering.RHI.RHICommandContext.Current.BindSampledTextures([new(0, retired, Dimension: VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL.OpenGLImageValues.Dimension(OpenGLImageValues.Target(((GpuTextureRef)retired).Target)))]);
        VanillaGraphicsExpanded.Rendering.RHI.RHICommandContext.Current.BindSampledTextures([new(2, retired, Dimension: VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL.OpenGLImageValues.Dimension(OpenGLImageValues.Target(((GpuTextureRef)retired).Target)))]);
        int retiredId = retired.TextureId;
        // Act: Delete the texture while it is cached, then upload through the surviving texture.
        retired.Dispose();
        // Assert: Retirement removes only deleted bindings, preserves the survivor, and leaves readable pixels.
        Assert.False(GL.IsTexture(retiredId));
        Assert.Equal(ErrorCode.NoError, GL.GetError());
        target.UploadDataImmediate(new float[] { .25f, .5f, .75f, 1 });
        Assert.Equal(ErrorCode.NoError, GL.GetError());
        Assert.Equal(0, OpenGlStateCache.Current.GetBoundTexture(TextureTarget.Texture2D, 2));
        Assert.Equal(target.TextureId, OpenGlStateCache.Current.GetBoundTexture(TextureTarget.Texture2D, 3));
        OpenGLImageBindings.ActiveTexture(OpenGlStateCache.Current, 0);
        Assert.Equal(0, GL.GetInteger(GetPName.TextureBinding2D));
        Assert.Equal(new float[] { .25f, .5f, .75f, 1 }, target.ReadPixels());
        Assert.Equal(ErrorCode.NoError, GL.GetError());
    }
    /// <summary>Background disposal preserves bindings until the render-thread queue actually deletes the texture.</summary>
    [Fact]
    public void QueuedDeletionUpdatesCacheOnlyWhenDrained()
    {
        // Arrange: Start with an empty deletion queue and bind a resource that will be retired off-thread.
        EnsureContextValid();
        using var manager = RHIResourceManagerSystem.EnsureCurrentContext();
        try
        {
            manager.OnRenderFrame(0, RHIResourceManager.Stage);
            using var target = RHICommandContext.Current.CreateDynamicTexture2D(new RHITextureDescription { Dimension = TextureDimension.Two, Format = TextureFormat.Rgba32f, Width = System.Math.Max(1, 1), Height = System.Math.Max(1, 1), MipLevels = 1, Storage = ResourceStorageMode.Mutable, Filter = TextureFilterMode.Nearest, BaseLevel = 0, MaxLevel = (1) - 1, Label = null }).RequireResource();
            using var retired = RHICommandContext.Current.CreateDynamicTexture2D(new RHITextureDescription { Dimension = TextureDimension.Two, Format = TextureFormat.Rgba32f, Width = System.Math.Max(1, 1), Height = System.Math.Max(1, 1), MipLevels = 1, Storage = ResourceStorageMode.Mutable, Filter = TextureFilterMode.Nearest, BaseLevel = 0, MaxLevel = (1) - 1, Label = null }).RequireResource();
            VanillaGraphicsExpanded.Rendering.RHI.RHICommandContext.Current.BindSampledTextures([new(0, retired, Dimension: VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL.OpenGLImageValues.Dimension(OpenGLImageValues.Target(((GpuTextureRef)retired).Target)))]);
            int retiredId = retired.TextureId;
            Exception? workerError = null;
            var worker = new Thread(() => { try { retired.Dispose(); } catch (Exception error) { workerError = error; } }) { IsBackground = true };
            // Act: Dispose off-thread so native deletion must wait for render-thread queue processing.
            worker.Start(); worker.Join();
            // Assert: Before draining, the resource and cached binding remain intact.
            Assert.Null(workerError);
            Assert.True(GL.IsTexture(retiredId));
            Assert.Equal(retiredId, OpenGlStateCache.Current.GetBoundTexture(TextureTarget.Texture2D, 0));
            // Act: Let the render-thread owner process the queued deletion.
            manager.OnRenderFrame(0, RHIResourceManager.Stage);
            // Assert: The queued name is deleted and unbound only after the owner processes the queue.
            Assert.False(GL.IsTexture(retiredId));
            Assert.Equal(0, OpenGlStateCache.Current.GetBoundTexture(TextureTarget.Texture2D, 0));
            target.UploadDataImmediate(new float[] { 0, 0, 0, 1 });
            Assert.Equal(ErrorCode.NoError, GL.GetError());
        }
        finally
        {
            RHIResourceManagerSystem.Shutdown();
            TextureStreamingSystem.Dispose();
        }
    }
    /// <summary>Retiring a typed borrower withdraws its handle without deleting or mutating the external image.</summary>
    [Fact]
    public void BorrowedTextureRetirementPreservesExternalStorage()
    {
        // Arrange: Keep the source texture owned separately while the borrower references its native handle.
        EnsureContextValid();
        using var external = RHICommandContext.Current.CreateDynamicTexture2D(new RHITextureDescription { Dimension = TextureDimension.Two, Width = 2, Height = 1, Format = TextureFormat.Rgba32f, MipLevels = 1, Storage = ResourceStorageMode.Mutable, BaseLevel = 0, MaxLevel = 0, Filter = TextureFilterMode.Nearest, Label = null }, [new RHITextureInitialData(new(new(), 0, 0, 0, 2, 1), new RHIPixelEncoding(TextureFormatHelper.GetPixelFormat(TextureFormat.Rgba32f), PixelComponentType.Float), System.Runtime.InteropServices.MemoryMarshal.AsBytes((new float[] { 8f,4f,2f,.3f,1f,2f,3f,.7f }).AsSpan()).ToArray())]).RequireResource();
        VanillaGraphicsExpanded.Rendering.RHI.RHICommandContext.Current.BindSampledTextures([new(3, external, Dimension: VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL.OpenGLImageValues.Dimension(OpenGLImageValues.Target(((GpuTextureRef)external).Target)))]);
        int handle = external.TextureId;
        using var borrowed = new BorrowedTexture(handle);
        Assert.Equal(handle,borrowed.TextureId);
        Assert.Equal((2,1),(borrowed.Width,borrowed.Height));
        Assert.True(borrowed.IsValid);
        Assert.Throws<NotSupportedException>(()=>borrowed.Detach());
        // Act: Dispose the borrower before inspecting the still-owned source image.
        borrowed.Dispose();
        // Assert: Borrower retirement clears its handle without unbinding, deleting, or changing source pixels.
        Assert.False(borrowed.IsValid);
        Assert.Equal(0,borrowed.TextureId);
        Assert.True(GL.IsTexture(handle));
        Assert.Equal(handle,OpenGlStateCache.Current.GetBoundTexture(TextureTarget.Texture2D,3));
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
        // Arrange: Allocate native storage for each target before constructing its managed borrowed view.
        EnsureContextValid();
        GL.CreateTextures(target,1,out int texture);
        try
        {
            if(target==TextureTarget.Texture1D) GL.TextureStorage1D(texture,3,SizedInternalFormat.Rgba16f,width);
            else GL.TextureStorage3D(texture,3,SizedInternalFormat.Rgba16f,width,height,depth);
            // Act: Read the metadata through a borrowed wrapper without transferring native ownership.
            using(var borrowed=new BorrowedTexture(texture))
            {
                // Assert: The wrapper reports the original target, dimensions, format, and allocated mip count.
                Assert.Equal(OpenGLImageValues.Dimension(target),borrowed.TextureTarget);
                Assert.Equal((width,height,depth),(borrowed.Width,borrowed.Height,borrowed.Depth));
                Assert.Equal(TextureFormat.Rgba16f,borrowed.InternalFormat);
                Assert.Equal(3,borrowed.StorageMipLevels);
            }
            Assert.True(GL.IsTexture(texture));
            Assert.Equal(ErrorCode.NoError,GL.GetError());
        }
        finally { RHICommandContext.Current.RetireResource(new(VanillaGraphicsExpanded.Rendering.GpuResourceKind.Texture, texture)); }
    }
    #endregion
    #endregion
}
