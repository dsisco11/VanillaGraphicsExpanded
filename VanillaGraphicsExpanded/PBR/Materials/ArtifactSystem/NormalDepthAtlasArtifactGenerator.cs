using System;
using System.Threading.Tasks;

using OpenTK.Graphics.OpenGL;

using VanillaGraphicsExpanded.Cache.ArtifactSystem;
using VanillaGraphicsExpanded.ModSystems;
using VanillaGraphicsExpanded.PBR.Materials.Cache;
using VanillaGraphicsExpanded.Rendering;

using Vintagestory.API.Client;
using Vintagestory.API.Common;

namespace VanillaGraphicsExpanded.PBR.Materials.ArtifactSystem;

internal sealed class NormalDepthAtlasArtifactGenerator
{
    private readonly ICoreClientAPI capi;
    private readonly IMaterialAtlasDiskCache diskCache;
    private readonly MaterialAtlasArtifactRenderQueue renderQueue;
    private readonly MaterialAtlasArtifactBuildTracker tracker;

    private readonly ArtifactScheduler<WorkKey, Output> scheduler;

    public NormalDepthAtlasArtifactGenerator(
        ICoreClientAPI capi,
        IMaterialAtlasDiskCache diskCache,
        MaterialAtlasArtifactRenderQueue renderQueue,
        MaterialAtlasArtifactBuildTracker tracker,
        int maxConcurrency = 1,
        int ioCapacity = 1,
        int gpuCapacity = 2)
    {
        this.capi = capi ?? throw new ArgumentNullException(nameof(capi));
        this.diskCache = diskCache ?? throw new ArgumentNullException(nameof(diskCache));
        this.renderQueue = renderQueue ?? throw new ArgumentNullException(nameof(renderQueue));
        this.tracker = tracker ?? throw new ArgumentNullException(nameof(tracker));

        var diskReservations = new ArtifactReservationPool(Math.Max(0, ioCapacity));
        var gpuReservations = new ArtifactReservationPool(Math.Max(0, gpuCapacity));

        scheduler = new ArtifactScheduler<WorkKey, Output>(
            capi,
            new Computer(diskCache),
            new OutputStage(capi, diskCache, renderQueue),
            new Applier(tracker),
            maxConcurrency: Math.Max(1, maxConcurrency),
            diskReservations: diskReservations,
            gpuReservations: gpuReservations);
    }

    public void Start() => scheduler.Start();

    public void Stop() => scheduler.Stop();

    public void BumpSession() => scheduler.BumpSession();

    public ArtifactSchedulerStats GetStatsSnapshot() => scheduler.GetStatsSnapshot();

    public bool Enqueue(WorkKey key) => scheduler.Enqueue(new WorkItem(key));

    internal enum JobKind
    {
        UploadCached = 0,
        Override = 1,
        Bake = 2,
    }

    internal readonly record struct WorkKey(
        int GenerationId,
        int AtlasTextureId,
        int NormalDepthTextureId,
        int AtlasWidth,
        int AtlasHeight,
        AtlasRect Rect,
        JobKind Kind,
        AssetLocation? TargetTexture,
        AssetLocation? OverrideTexture,
        float NormalScale,
        float DepthScale,
        string? RuleId,
        AssetLocation? RuleSource,
        bool EnableCache,
        AtlasCacheKey CacheKey,
        int Priority);

    internal readonly record struct Output(
        int GenerationId,
        int AtlasTextureId,
        int NormalDepthTextureId,
        int AtlasWidth,
        int AtlasHeight,
        AtlasRect Rect,
        JobKind Kind,
        float[]? RgbaQuads,
        bool StoreToDisk,
        AtlasCacheKey CacheKey,
        float NormalScale,
        float DepthScale,
        int Priority);

    private sealed class WorkItem : IArtifactWorkItem<WorkKey>
    {
        public WorkItem(WorkKey key) => Key = key;

        public WorkKey Key { get; }

        public int Priority => Key.Priority;

        public string TypeId => "NormalDepthAtlas";

        public string DebugLabel => Key.TargetTexture?.ToString() ?? $"page:{Key.AtlasTextureId}";

        public ArtifactOutputKinds RequiredOutputKinds => ArtifactOutputKinds.Disk | ArtifactOutputKinds.Gpu;
    }

    private sealed class Computer : IArtifactComputer<WorkKey, Output>
    {
        private readonly IMaterialAtlasDiskCache diskCache;
        private readonly MaterialOverrideTextureLoader overrideLoader = new();

        public Computer(IMaterialAtlasDiskCache diskCache)
        {
            this.diskCache = diskCache;
        }

        public ValueTask<ArtifactComputeResult<Output>> ComputeAsync(ArtifactComputeContext<WorkKey> context)
        {
            WorkKey key = context.Key;

            if (context.Session.CancellationToken.IsCancellationRequested)
            {
                return ValueTask.FromResult(new ArtifactComputeResult<Output>(IsNoop: true, Output: default, RequiresApply: false));
            }

            if (key.Kind == JobKind.UploadCached && key.EnableCache && key.CacheKey.SchemaVersion != 0
                && diskCache.TryLoadNormalDepthTile(key.CacheKey, out float[] cached))
            {
                var output = new Output(
                    key.GenerationId,
                    key.AtlasTextureId,
                    key.NormalDepthTextureId,
                    key.AtlasWidth,
                    key.AtlasHeight,
                    key.Rect,
                    JobKind.UploadCached,
                    cached,
                    StoreToDisk: false,
                    CacheKey: default,
                    key.NormalScale,
                    key.DepthScale,
                    key.Priority);

                return ValueTask.FromResult(new ArtifactComputeResult<Output>(IsNoop: false, Output: new Optional<Output>(output), RequiresApply: true));
            }

            if (key.Kind == JobKind.Override && key.OverrideTexture is not null)
            {
                if (key.EnableCache && key.CacheKey.SchemaVersion != 0 && diskCache.TryLoadNormalDepthTile(key.CacheKey, out float[] cachedOv))
                {
                    var ovcached = new Output(
                        key.GenerationId,
                        key.AtlasTextureId,
                        key.NormalDepthTextureId,
                        key.AtlasWidth,
                        key.AtlasHeight,
                        key.Rect,
                        JobKind.Override,
                        cachedOv,
                        StoreToDisk: false,
                        CacheKey: default,
                        key.NormalScale,
                        key.DepthScale,
                        key.Priority);

                    return ValueTask.FromResult(new ArtifactComputeResult<Output>(IsNoop: false, Output: new Optional<Output>(ovcached), RequiresApply: true));
                }

                if (overrideLoader.TryLoadRgbaFloats01(
                        context.Capi,
                        key.OverrideTexture,
                        out int _,
                        out int _,
                        out float[] rgba01,
                        out string? _,
                        expectedWidth: key.Rect.Width,
                        expectedHeight: key.Rect.Height))
                {
                    bool isIdentity = key.NormalScale == 1f && key.DepthScale == 1f;
                    float[] uploadData = rgba01;

                    if (!isIdentity)
                    {
                        int floats = checked(key.Rect.Width * key.Rect.Height * 4);
                        var scaled = new float[floats];
                        Array.Copy(rgba01, 0, scaled, 0, floats);
                        VanillaGraphicsExpanded.Numerics.SimdSpanMath.MultiplyClamp01Interleaved4InPlace2D(
                            destination4: scaled,
                            rectWidthPixels: key.Rect.Width,
                            rectHeightPixels: key.Rect.Height,
                            rowStridePixels: key.Rect.Width,
                            mulRgb: key.NormalScale,
                            mulA: key.DepthScale);

                        uploadData = scaled;
                    }

                    var output = new Output(
                        key.GenerationId,
                        key.AtlasTextureId,
                        key.NormalDepthTextureId,
                        key.AtlasWidth,
                        key.AtlasHeight,
                        key.Rect,
                        JobKind.Override,
                        uploadData,
                        StoreToDisk: key.EnableCache && key.CacheKey.SchemaVersion != 0,
                        CacheKey: key.CacheKey,
                        key.NormalScale,
                        key.DepthScale,
                        key.Priority);

                    return ValueTask.FromResult(new ArtifactComputeResult<Output>(IsNoop: false, Output: new Optional<Output>(output), RequiresApply: true));
                }

                // Failed override load -> noop.
                return ValueTask.FromResult(new ArtifactComputeResult<Output>(IsNoop: true, Output: default, RequiresApply: true));
            }

            if (key.Kind == JobKind.Bake)
            {
                if (key.EnableCache && key.CacheKey.SchemaVersion != 0 && diskCache.TryLoadNormalDepthTile(key.CacheKey, out float[] bakedCached))
                {
                    var cachedOutput = new Output(
                        key.GenerationId,
                        key.AtlasTextureId,
                        key.NormalDepthTextureId,
                        key.AtlasWidth,
                        key.AtlasHeight,
                        key.Rect,
                        JobKind.UploadCached,
                        bakedCached,
                        StoreToDisk: false,
                        CacheKey: default,
                        key.NormalScale,
                        key.DepthScale,
                        key.Priority);

                    return ValueTask.FromResult(new ArtifactComputeResult<Output>(IsNoop: false, Output: new Optional<Output>(cachedOutput), RequiresApply: true));
                }

                // Bake is executed on the render thread in the output stage.
                var output = new Output(
                    key.GenerationId,
                    key.AtlasTextureId,
                    key.NormalDepthTextureId,
                    key.AtlasWidth,
                    key.AtlasHeight,
                    key.Rect,
                    JobKind.Bake,
                    RgbaQuads: null,
                    StoreToDisk: key.EnableCache && key.CacheKey.SchemaVersion != 0,
                    CacheKey: key.CacheKey,
                    key.NormalScale,
                    key.DepthScale,
                    key.Priority);

                return ValueTask.FromResult(new ArtifactComputeResult<Output>(IsNoop: false, Output: new Optional<Output>(output), RequiresApply: true));
            }

            return ValueTask.FromResult(new ArtifactComputeResult<Output>(IsNoop: true, Output: default, RequiresApply: true));
        }
    }

    private sealed class OutputStage : IArtifactOutputStage<WorkKey, Output>
    {
        private readonly ICoreClientAPI capi;
        private readonly IMaterialAtlasDiskCache diskCache;
        private readonly MaterialAtlasArtifactRenderQueue renderQueue;
        public OutputStage(ICoreClientAPI capi, IMaterialAtlasDiskCache diskCache, MaterialAtlasArtifactRenderQueue renderQueue)
        {
            this.capi = capi;
            this.diskCache = diskCache;
            this.renderQueue = renderQueue;
        }

        public async ValueTask OutputAsync(ArtifactOutputContext<WorkKey, Output> context)
        {
            if (context.Session.CancellationToken.IsCancellationRequested)
            {
                return;
            }

            // Page clear is only needed for bake jobs.
            // When cache warmup has already populated parts of the atlas during loading,
            // clearing here would wipe those warmed tiles and force redundant uploads.
            if (context.Output.Kind == JobKind.Bake
                && !ConfigModSystem.Config.MaterialAtlas.ForceCacheWarmupDirectUploadsOnWorldLoad)
            {
                await renderQueue.EnsureNormalDepthPageClearedAsync(
                    capi,
                    context.Output.GenerationId,
                    context.Output.AtlasTextureId,
                    context.Output.AtlasWidth,
                    context.Output.AtlasHeight,
                    context.Session.CancellationToken).ConfigureAwait(false);
            }

            if (context.Output.Kind == JobKind.Bake)
            {
                float[]? baked = await renderQueue.BakeAndReadbackAsync(
                    capi,
                    context.Output.GenerationId,
                    context.Output.AtlasTextureId,
                    context.Output.AtlasWidth,
                    context.Output.AtlasHeight,
                    context.Output.Rect,
                    context.Output.NormalScale,
                    context.Output.DepthScale,
                    context.Session.CancellationToken).ConfigureAwait(false);

                if (baked is null)
                {
                    return;
                }

                if (context.Output.StoreToDisk && context.Output.CacheKey.SchemaVersion != 0)
                {
                    diskCache.StoreNormalDepthTile(context.Output.CacheKey, context.Output.Rect.Width, context.Output.Rect.Height, baked);
                }

                return;
            }

            float[]? rgba = context.Output.RgbaQuads;
            if (rgba is null || rgba.Length != checked(context.Output.Rect.Width * context.Output.Rect.Height * 4))
            {
                return;
            }

            _ = TextureStreamingSystem.StageCopy(
                textureId: context.Output.NormalDepthTextureId,
                target: TextureUploadTarget.For2D(),
                region: TextureUploadRegion.For2D(context.Output.Rect.X, context.Output.Rect.Y, context.Output.Rect.Width, context.Output.Rect.Height),
                pixelFormat: PixelFormat.Rgba,
                pixelType: PixelType.Float,
                data: rgba,
                priority: TextureUploadPriority.Normal,
                unpackAlignment: 4);

            if (context.Output.StoreToDisk && context.Output.CacheKey.SchemaVersion != 0)
            {
                diskCache.StoreNormalDepthTile(context.Output.CacheKey, context.Output.Rect.Width, context.Output.Rect.Height, rgba);
            }
        }
    }

    private sealed class Applier : IArtifactApplier<WorkKey, Output>
    {
        private readonly MaterialAtlasArtifactBuildTracker tracker;

        public Applier(MaterialAtlasArtifactBuildTracker tracker)
        {
            this.tracker = tracker;
        }

        public void Apply(in ArtifactApplyContext<WorkKey, Output> context)
        {
            tracker.CompleteOne(context.Key.GenerationId);
        }
    }
}
