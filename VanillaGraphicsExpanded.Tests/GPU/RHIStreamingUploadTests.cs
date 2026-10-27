using System;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Rendering.RHI;
using VanillaGraphicsExpanded.Tests.GPU.Fixtures;

namespace VanillaGraphicsExpanded.Tests.GPU;

/// <summary>Checks the streaming queue's typed immediate and staged RHI uploads against actual image contents.</summary>
[Collection("GPU")]
[Trait("Category", "GPU")]
public sealed class RHIStreamingUploadTests(HeadlessGLFixture fixture)
{
    #region Public API
    /// <summary>Padded client rows and staged PBO rows produce the same contents and preserve borrowed unpack selectors.</summary>
    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public void StreamingPreservesPaddedRowsAndUnpackState(bool staged, bool rgb)
    {
        fixture.EnsureContextValid(); fixture.InitializeResourceDisposal();
        var cache = StateCache.Current;
        int priorAlignment = GL.GetInteger(GetPName.UnpackAlignment);
        int priorRowLength = GL.GetInteger(GetPName.UnpackRowLength);
        int priorBuffer = GL.GetInteger(GetPName.PixelUnpackBufferBinding);
        try
        {
            var context = RHICommandContext.Current;
            int width = rgb ? 1 : 2;
            using var image = context.CreateDynamicTexture2D(new RHITextureDescription
                { Dimension = TextureDimension.Two, Width = width, Height = 2, Format = rgb ? TextureFormat.Rgb8 : TextureFormat.R8,
                  MipLevels = 1, Storage = ResourceStorageMode.Mutable }).RequireResource();
            using var sentinel = context.CreatePixelUnpackBuffer(new RHIBufferDescription
                { Role = BufferRole.PixelUnpack, Mode = ResourceStorageMode.Mutable, Capacity = 32, Usage = BufferUsage.StreamDraw }).RequireResource();
            using var streaming = new TextureStreamingManager(TextureStreamingSettings.Default with
                { EnablePboStreaming = staged, ForceDisablePersistent = true, MaxFrameBudgetMs = 0,
                  PersistentRingBytes = 1024, TripleBufferBytes = 1024 });
            OpenTK.Graphics.OpenGL4.GL.BindBuffer(OpenTK.Graphics.OpenGL4.BufferTarget.PixelUnpackBuffer, sentinel.BufferId);
            GL.PixelStore(PixelStoreParameter.UnpackAlignment, 8);
            GL.PixelStore(PixelStoreParameter.UnpackRowLength, 7);
            cache.Invalidate(EPipelineState.PixelUnpack | EPipelineState.BufferBindings);
            byte[] bytes = rgb ? [11, 12, 13, 0, 21, 22, 23, 0] : [11, 12, 0, 0, 21, 22, 0, 0];
            streaming.Enqueue(new(image.TextureId, new(TextureTarget.Texture2D, TextureTarget.Texture2D),
                new(0, 0, 0, width, 2, 1, 0), rgb ? PixelFormat.Rgb : PixelFormat.Red, PixelType.UnsignedByte,
                TextureUploadData.From(bytes), UnpackAlignment: rgb ? 4 : 1, UnpackRowLength: rgb ? 1 : 4, TextureOwner: image));
            streaming.TickOnRenderThread();
            Assert.Equal(0, streaming.PendingCount);
            Assert.Equal(staged ? TextureStreamingBackendKind.TripleBuffered : TextureStreamingBackendKind.None, streaming.Backend);
            Assert.Equal(sentinel.BufferId, GL.GetInteger(GetPName.PixelUnpackBufferBinding));
            Assert.Equal(8, GL.GetInteger(GetPName.UnpackAlignment));
            Assert.Equal(7, GL.GetInteger(GetPName.UnpackRowLength));
            using var readback = context.EnqueueReadback(new(image, new(new(0), 0, 0, 0, width, 2, 1),
                new(rgb ? PixelChannelLayout.Rgb : PixelChannelLayout.Red, PixelComponentType.UnsignedByte)));
            Assert.Equal(CompletionStatus.Ready, readback.Wait(TimeSpan.FromSeconds(5)));
            using var mapped = readback.MapReadyReadback();
            Assert.Equal(rgb ? new byte[] { 11, 12, 13, 21, 22, 23 } : new byte[] { 11, 12, 21, 22 }, mapped.Bytes.ToArray());
            Assert.Equal(ErrorCode.NoError, GL.GetError());
        }
        finally
        {
            GL.BindBuffer(BufferTarget.PixelUnpackBuffer, priorBuffer);
            GL.PixelStore(PixelStoreParameter.UnpackAlignment, priorAlignment);
            GL.PixelStore(PixelStoreParameter.UnpackRowLength, priorRowLength);
            cache.InvalidateAll(); fixture.CleanupGpuResources();
        }
    }
    #endregion
}
