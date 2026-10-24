using System.Reflection;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.PBR.Materials;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Tests.GPU.Fixtures;

namespace VanillaGraphicsExpanded.Tests.GPU;

/// <summary>Checks actual page-bound publication and queued transfer ownership across retirement.</summary>
[Collection("GPU")]
[Trait("Category", "GPU")]
public sealed class MaterialAtlasPublicationLifetimeTests(HeadlessGLFixture fixture) : RenderTestBase(fixture)
{
    #region Public API
    /// <summary>A live texture owner cannot authorize a queued write addressed to a different live texture name.</summary>
    [Fact]
    public void StreamingTransferRejectsLiveOwnerNameMismatch()
    {
        EnsureContextValid();
        TextureStreamingSystem.Dispose();
        try
        {
            using var store = new MaterialAtlasTextureStore();
            store.SyncToAtlasPages([(7, 1, 1), (8, 1, 1)], false);
            Assert.True(store.TryGetPageTextures(7, out var owner));
            Assert.True(store.TryGetPageTextures(8, out var destination));
            float[] before = destination.MaterialParamsTexture.ReadPixels();
            var stage = TextureStreamingSystem.StageCopy(destination.MaterialParamsTexture.TextureId,
                TextureUploadTarget.For2D(), TextureUploadRegion.For2D(0, 0, 1, 1), PixelFormat.Rgba, PixelType.Float,
                new float[] { .5f, .25f, 8, 0 }, textureOwner: owner.MaterialParamsTexture);
            Assert.NotEqual(TextureStageOutcome.Rejected, stage.Outcome);
            DrainUploads();
#if DEBUG
            Assert.True(TextureStreamingSystem.GetDiagnosticsSnapshot().DroppedInvalid > 0);
#endif
            Assert.Equal(before, destination.MaterialParamsTexture.ReadPixels());
        }
        finally { TextureStreamingSystem.Dispose(); }
    }

    /// <summary>Inline and queued writes publish their envelope immediately and target only the captured live page.</summary>
    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void ExactPagePublicationIncludesPendingEmissionAndRejectsRetirement(bool immediate)
    {
        EnsureContextValid();
        TextureStreamingSystem.Dispose();
        try
        {
            using var store = new MaterialAtlasTextureStore();
            store.SyncToAtlasPages([(7, 1, 1)], false);
            Assert.True(store.TryGetPageTextures(7, out var page));
            Assert.True(page.TryPublishMaterial(page.MaterialParamsTexture.TextureId, new(0, 0, 1, 1),
                [.5f, .25f, 8, .125f], immediate));
            Assert.Equal(256, store.MaximumEmissionNits);
            DrainUploads();
            Assert.Equal(new float[] { .5f, .25f, 8, .125f }, page.MaterialParamsTexture.ReadPixels());
            store.ReplaceMaterialParamsTextureWithData(7, 1, 1, [.5f, .25f, 0]);
            Assert.True(store.TryGetPageTextures(7, out var replacement));
            float[] pixels = replacement.MaterialParamsTexture.ReadPixels();
            Assert.False(page.TryPublishMaterial(replacement.MaterialParamsTexture.TextureId,
                new(0, 0, 1, 1), [.5f, .25f, 16, .125f], immediate));
            Assert.Equal(pixels, replacement.MaterialParamsTexture.ReadPixels());
            Assert.Equal(256, store.MaximumEmissionNits);
        }
        finally { TextureStreamingSystem.Dispose(); }
    }

    /// <summary>Ordinary, mapped, chunked and overflow transfers drop retired owners before writing a replacement page.</summary>
    [Theory]
    [InlineData(0)] [InlineData(1)] [InlineData(2)] [InlineData(3)] [InlineData(4)]
    public void QueuedTransferCannotOutliveCapturedMaterialPage(int route)
    {
        EnsureContextValid();
        TextureStreamingSystem.Dispose();
        try
        {
            var manager = TextureStreamingSystem.Manager;
            manager.Settings = TextureStreamingSettings.Default with
            {
                EnablePboStreaming = route != 0,
                MaxStagingBytes = route is 2 or 4 ? 16 : 1024,
                MaxUploadsPerFrame = 32, MaxFrameBudgetMs = 0,
                PersistentRingBytes = 4096, TripleBufferBytes = 4096
            };
            // A one-slot existing queue exercises overflow without tens of thousands of uploads.
            if (route is 3 or 4)
                typeof(TextureStreamingManager).GetField("commandQueue", BindingFlags.Instance | BindingFlags.NonPublic)!
                    .SetValue(manager, new SingleChannelUploadQueue(1));
            using var store = new MaterialAtlasTextureStore();
            store.SyncToAtlasPages([(7, 1, 4)], false);
            Assert.True(store.TryGetPageTextures(7, out var page));
            float[] dark = Enumerable.Range(0, 4).SelectMany(_ => new float[] { .5f, .25f, 0, 0 }).ToArray();
            if (route == 1)
            {
                Assert.True(page.TryPublishMaterial(page.MaterialParamsTexture.TextureId, new(0, 0, 1, 4), dark, false));
                DrainUploads();
            }
            else if (route == 3)
                Assert.True(page.TryPublishMaterial(page.MaterialParamsTexture.TextureId, new(0, 0, 1, 4), dark, false));
            float[] bright = Enumerable.Range(0, 4).SelectMany(_ => new float[] { .5f, .25f, 8, .125f }).ToArray();
            Assert.True(page.TryPublishMaterial(page.MaterialParamsTexture.TextureId, new(0, 0, 1, 4), bright, false));
            int pending = manager.PendingCount;
            Assert.True(pending > 0);
            if (route is 2 or 4) Assert.True(pending >= 4);
#if DEBUG
            if (route is 3 or 4) Assert.True(manager.GetDiagnosticsSnapshot().Fallback.QueueFull > 0);
            if (route == 1) Assert.True(manager.GetDiagnosticsSnapshot().StageCopyStaged > 0);
#endif
            store.ReplaceMaterialParamsTextureWithData(7, 1, 4,
                Enumerable.Range(0, 4).SelectMany(_ => new float[] { .5f, .25f, 0 }).ToArray());
            Assert.True(store.TryGetPageTextures(7, out var replacement));
            int identity = replacement.MaterialParamsTexture.TextureId;
            float[] retained = replacement.MaterialParamsTexture.ReadPixels();
            DrainUploads();
#if DEBUG
            Assert.True(manager.GetDiagnosticsSnapshot().DroppedInvalid > 0);
#endif
            Assert.Equal(identity, replacement.MaterialParamsTexture.TextureId);
            Assert.Equal(retained, replacement.MaterialParamsTexture.ReadPixels());
            Assert.Equal(256, store.MaximumEmissionNits);
        }
        finally { TextureStreamingSystem.Dispose(); }
    }
    #endregion

    #region Private
    /// <summary>Drains the bounded test queue, including cold-backend fallback and reclaimed mapped transfers.</summary>
    private static void DrainUploads()
    {
        for (int tick = 0; tick < 32 && TextureStreamingSystem.Manager.PendingCount > 0; tick++)
            TextureStreamingSystem.TickOnRenderThread();
        Assert.Equal(0, TextureStreamingSystem.Manager.PendingCount);
    }
    #endregion
}
