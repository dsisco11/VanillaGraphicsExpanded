using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;

using VanillaGraphicsExpanded.PBR.Materials.Async;

using Vintagestory.API.Client;

namespace VanillaGraphicsExpanded.PBR.Materials.Artifacts;

/// <summary>
/// Render-thread executor for artifact jobs that must execute GL work (normal/depth page clears, baking, readback).
/// </summary>
internal sealed class MaterialAtlasArtifactRenderQueue
{
    private readonly Func<int, MaterialAtlasPageTextures?> tryGetPageTextures;

    private readonly ConcurrentQueue<IRenderWorkItem> pending = new();
    private readonly ConcurrentDictionary<(int generationId, int atlasTexId), Task> pageClearTasks = new();

    public MaterialAtlasArtifactRenderQueue(Func<int, MaterialAtlasPageTextures?> tryGetPageTextures)
    {
        this.tryGetPageTextures = tryGetPageTextures ?? throw new ArgumentNullException(nameof(tryGetPageTextures));
    }

    public Task EnsureNormalDepthPageClearedAsync(
        ICoreClientAPI capi,
        int generationId,
        int atlasTextureId,
        int atlasWidth,
        int atlasHeight,
        CancellationToken token)
    {
        if (token.IsCancellationRequested)
        {
            return Task.CompletedTask;
        }

        var key = (generationId, atlasTextureId);

        return pageClearTasks.GetOrAdd(key, _ =>
        {
            var tcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            pending.Enqueue(new ClearPageWorkItem(capi, atlasTextureId, atlasWidth, atlasHeight, tcs, tryGetPageTextures));
            return tcs.Task;
        });
    }

    public Task<float[]?> BakeAndReadbackAsync(
        ICoreClientAPI capi,
        int generationId,
        int atlasTextureId,
        int atlasWidth,
        int atlasHeight,
        AtlasRect rect,
        float normalScale,
        float depthScale,
        CancellationToken token)
    {
        if (token.IsCancellationRequested)
        {
            return Task.FromResult<float[]?>(null);
        }

        var tcs = new TaskCompletionSource<float[]?>(TaskCreationOptions.RunContinuationsAsynchronously);
        pending.Enqueue(new BakeWorkItem(
            capi,
            generationId,
            atlasTextureId,
            atlasWidth,
            atlasHeight,
            rect,
            normalScale,
            depthScale,
            tcs,
            tryGetPageTextures));

        return tcs.Task;
    }

    public void Drain(int maxItemsPerFrame = 8)
    {
        int budget = maxItemsPerFrame <= 0 ? 8 : maxItemsPerFrame;

        while (budget-- > 0 && pending.TryDequeue(out IRenderWorkItem? item))
        {
            try
            {
                item.Execute();
            }
            catch
            {
                // Best-effort.
                try { item.Fail(); } catch { }
            }
        }
    }

    private interface IRenderWorkItem
    {
        void Execute();
        void Fail();
    }

    private sealed class ClearPageWorkItem : IRenderWorkItem
    {
        private readonly ICoreClientAPI capi;
        private readonly int atlasTextureId;
        private readonly int atlasWidth;
        private readonly int atlasHeight;
        private readonly TaskCompletionSource tcs;
        private readonly Func<int, MaterialAtlasPageTextures?> tryGetPageTextures;

        public ClearPageWorkItem(
            ICoreClientAPI capi,
            int atlasTextureId,
            int atlasWidth,
            int atlasHeight,
            TaskCompletionSource tcs,
            Func<int, MaterialAtlasPageTextures?> tryGetPageTextures)
        {
            this.capi = capi;
            this.atlasTextureId = atlasTextureId;
            this.atlasWidth = atlasWidth;
            this.atlasHeight = atlasHeight;
            this.tcs = tcs;
            this.tryGetPageTextures = tryGetPageTextures;
        }

        public void Execute()
        {
            // Page textures may not exist for the atlas page; skip if missing.
            MaterialAtlasPageTextures? pageTextures = tryGetPageTextures(atlasTextureId);
            if (pageTextures?.NormalDepthTexture is null || !pageTextures.NormalDepthTexture.IsValid)
            {
                tcs.TrySetResult();
                return;
            }

            MaterialAtlasNormalDepthGpuBuilder.ClearAtlasPage(
                capi,
                destNormalDepthTexId: pageTextures.NormalDepthTexture.TextureId,
                atlasWidth: atlasWidth,
                atlasHeight: atlasHeight);

            tcs.TrySetResult();
        }

        public void Fail() => tcs.TrySetResult();
    }

    private sealed class BakeWorkItem : IRenderWorkItem
    {
        private readonly ICoreClientAPI capi;
        private readonly int generationId;
        private readonly int atlasTextureId;
        private readonly int atlasWidth;
        private readonly int atlasHeight;
        private readonly AtlasRect rect;
        private readonly float normalScale;
        private readonly float depthScale;
        private readonly TaskCompletionSource<float[]?> tcs;
        private readonly Func<int, MaterialAtlasPageTextures?> tryGetPageTextures;

        public BakeWorkItem(
            ICoreClientAPI capi,
            int generationId,
            int atlasTextureId,
            int atlasWidth,
            int atlasHeight,
            AtlasRect rect,
            float normalScale,
            float depthScale,
            TaskCompletionSource<float[]?> tcs,
            Func<int, MaterialAtlasPageTextures?> tryGetPageTextures)
        {
            this.capi = capi;
            this.generationId = generationId;
            this.atlasTextureId = atlasTextureId;
            this.atlasWidth = atlasWidth;
            this.atlasHeight = atlasHeight;
            this.rect = rect;
            this.normalScale = normalScale;
            this.depthScale = depthScale;
            this.tcs = tcs;
            this.tryGetPageTextures = tryGetPageTextures;
        }

        public void Execute()
        {
            MaterialAtlasPageTextures? pageTextures = tryGetPageTextures(atlasTextureId);
            if (pageTextures?.NormalDepthTexture is null || !pageTextures.NormalDepthTexture.IsValid)
            {
                tcs.TrySetResult(null);
                return;
            }

            _ = MaterialAtlasNormalDepthGpuBuilder.BakePerRect(
                capi,
                baseAlbedoAtlasPageTexId: atlasTextureId,
                destNormalDepthTexId: pageTextures.NormalDepthTexture.TextureId,
                atlasWidth: atlasWidth,
                atlasHeight: atlasHeight,
                rectX: rect.X,
                rectY: rect.Y,
                rectWidth: rect.Width,
                rectHeight: rect.Height,
                normalScale: normalScale,
                depthScale: depthScale);

            float[] rgbaQuads = pageTextures.NormalDepthTexture.ReadPixelsRegion(rect.X, rect.Y, rect.Width, rect.Height);
            if (rgbaQuads.Length != checked(rect.Width * rect.Height * 4))
            {
                tcs.TrySetResult(null);
                return;
            }

            tcs.TrySetResult(rgbaQuads);
        }

        public void Fail() => tcs.TrySetResult(null);
    }
}
