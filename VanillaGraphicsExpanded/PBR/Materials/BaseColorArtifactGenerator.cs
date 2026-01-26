using System;
using System.Collections.Generic;
using System.Numerics;
using System.Threading.Tasks;

using VanillaGraphicsExpanded.Cache;
using VanillaGraphicsExpanded.Cache.Artifacts;
using VanillaGraphicsExpanded.Imaging;
using VanillaGraphicsExpanded.PBR.Materials.Cache;

using Vintagestory.API.Client;
using Vintagestory.API.Common;

namespace VanillaGraphicsExpanded.PBR.Materials;

internal sealed class BaseColorArtifactGenerator
{
    private readonly ICoreClientAPI capi;
    private readonly PbrMaterialRegistry registry;
    private readonly IDataCacheSystem<AtlasCacheKey, BaseColorRgb16f> diskCache;
    private readonly BaseColorCacheKeyInputs keyInputs;
    private readonly BaseColorCacheKeyBuilder keyBuilder;
    private readonly long regenSessionId;

    private readonly ArtifactScheduler<BaseColorWorkKey, BaseColorArtifactOutput> scheduler;

    public BaseColorArtifactGenerator(
        ICoreClientAPI capi,
        PbrMaterialRegistry registry,
        IDataCacheSystem<AtlasCacheKey, BaseColorRgb16f> diskCache,
        BaseColorCacheKeyInputs keyInputs,
        BaseColorCacheKeyBuilder keyBuilder,
        long regenSessionId,
        int maxConcurrency = 1,
        int ioCapacity = 1)
    {
        this.capi = capi ?? throw new ArgumentNullException(nameof(capi));
        this.registry = registry ?? throw new ArgumentNullException(nameof(registry));
        this.diskCache = diskCache ?? throw new ArgumentNullException(nameof(diskCache));
        this.keyInputs = keyInputs;
        this.keyBuilder = keyBuilder ?? throw new ArgumentNullException(nameof(keyBuilder));
        this.regenSessionId = regenSessionId;

        var diskReservations = new ArtifactReservationPool(Math.Max(0, ioCapacity));

        scheduler = new ArtifactScheduler<BaseColorWorkKey, BaseColorArtifactOutput>(
            capi: capi,
            computer: new Computer(),
            outputStage: new OutputStage(diskCache),
            applier: new Applier(registry, regenSessionId),
            maxConcurrency: Math.Max(1, maxConcurrency),
            diskReservations: diskReservations,
            gpuReservations: null);
    }

    public void Start() => scheduler.Start();

    public void Stop() => scheduler.Stop();

    public void StopAndLogSummary()
    {
        ArtifactSchedulerStats stats = scheduler.GetStatsSnapshot();
        scheduler.Stop();

        capi.Logger.Debug(
            "[VGE] BaseColor artifacts stopped: queued={0}, inflight={1}, completed={2}, errors={3}, avgComputeMs={4:F2}, avgOutputMs={5:F2}",
            stats.Queued,
            stats.InFlight,
            stats.Completed,
            stats.Errors,
            stats.AvgComputeMs,
            stats.AvgOutputMs);
    }

    public ArtifactSchedulerStats GetStatsSnapshot() => scheduler.GetStatsSnapshot();

    public int EnqueueWorklist(IEnumerable<AssetLocation> textures)
    {
        int enqueued = 0;

        foreach (AssetLocation texture in textures)
        {
            IAsset? asset = capi.Assets.TryGet(texture, loadAsset: true);
            if (asset is null)
            {
                continue;
            }

            ExtractAssetFingerprint(asset, out AssetLocation texLoc, out string? originPath, out long bytes);
            AtlasCacheKey cacheKey = keyBuilder.BuildKey(keyInputs, texLoc, originPath, bytes);

            // In-memory hit.
            if (registry.TryGetBaseColorInMemory(cacheKey, out _))
            {
                continue;
            }

            // On-disk hit -> apply to memory without scheduling.
            if (diskCache.TryGet(cacheKey, out BaseColorRgb16f onDisk))
            {
                Vector3 rgb = DecodeRgb16f(onDisk);
                capi.Event.EnqueueMainThreadTask(() =>
                {
                    registry.TryApplyBaseColorRegenResultForTests(regenSessionId, cacheKey, rgb);
                }, "vge-basecolor-cache-apply-hit");

                continue;
            }

            var workKey = new BaseColorWorkKey(texLoc, originPath, bytes, cacheKey);
            var item = new WorkItem(workKey);

            if (scheduler.Enqueue(item))
            {
                enqueued++;
            }
        }

        return enqueued;
    }

    internal readonly record struct BaseColorWorkKey(AssetLocation Texture, string? OriginPath, long Bytes, AtlasCacheKey CacheKey);

    internal readonly record struct BaseColorArtifactOutput(Vector3 LinearRgb, BaseColorRgb16f DiskPayload);

    private sealed class WorkItem : IArtifactWorkItem<BaseColorWorkKey>
    {
        public WorkItem(BaseColorWorkKey key)
        {
            Key = key;
        }

        public BaseColorWorkKey Key { get; }

        public int Priority => 0;

        public string TypeId => "BaseColor";

        public string DebugLabel => Key.Texture.ToString();

        public ArtifactOutputKinds RequiredOutputKinds => ArtifactOutputKinds.Disk;
    }

    private sealed class Computer : IArtifactComputer<BaseColorWorkKey, BaseColorArtifactOutput>
    {
        public Computer()
        {
        }

        public ValueTask<ArtifactComputeResult<BaseColorArtifactOutput>> ComputeAsync(ArtifactComputeContext<BaseColorWorkKey> context)
        {
            IAsset? asset = context.Capi.Assets.TryGet(context.Key.Texture, loadAsset: true);
            if (asset is null)
            {
                return ValueTask.FromResult(new ArtifactComputeResult<BaseColorArtifactOutput>(IsNoop: true, Output: default, RequiresApply: false));
            }

            if (!TryComputeAverageAlbedoLinearNoCache(context.Capi, asset, out Vector3 baseColorLinear, out _))
            {
                return ValueTask.FromResult(new ArtifactComputeResult<BaseColorArtifactOutput>(IsNoop: true, Output: default, RequiresApply: false));
            }

            Vector3 clamped = Vector3.Clamp(baseColorLinear, Vector3.Zero, Vector3.One);
            BaseColorRgb16f payload = EncodeRgb16f(clamped);

            var output = new BaseColorArtifactOutput(clamped, payload);
            return ValueTask.FromResult(new ArtifactComputeResult<BaseColorArtifactOutput>(IsNoop: false, Output: new Optional<BaseColorArtifactOutput>(output), RequiresApply: true));
        }

        private static bool TryComputeAverageAlbedoLinearNoCache(
            ICoreClientAPI capi,
            IAsset asset,
            out Vector3 baseColorLinear,
            out string? reason)
        {
            baseColorLinear = default;
            reason = null;

            try
            {
                using BitmapRef bmp = asset.ToBitmap(capi);

                int width = bmp.Width;
                int height = bmp.Height;
                int[] pixels = bmp.Pixels;

                if (pixels is null || pixels.Length < width * height)
                {
                    reason = "bitmap decode returned insufficient pixel data";
                    return false;
                }

                return AlbedoAverager.TryComputeAverageLinearRgb(
                    argbPixels: pixels,
                    width: width,
                    height: height,
                    averageLinearRgb: out baseColorLinear,
                    reason: out reason);
            }
            catch (Exception ex)
            {
                reason = ex.Message;
                return false;
            }
        }
    }

    private sealed class OutputStage : IArtifactOutputStage<BaseColorWorkKey, BaseColorArtifactOutput>
    {
        private readonly IDataCacheSystem<AtlasCacheKey, BaseColorRgb16f> diskCache;

        public OutputStage(IDataCacheSystem<AtlasCacheKey, BaseColorRgb16f> diskCache)
        {
            this.diskCache = diskCache;
        }

        public ValueTask OutputAsync(ArtifactOutputContext<BaseColorWorkKey, BaseColorArtifactOutput> context)
        {
            if (context.Session.CancellationToken.IsCancellationRequested)
            {
                return ValueTask.CompletedTask;
            }

            diskCache.Store(context.Key.CacheKey, context.Output.DiskPayload);
            return ValueTask.CompletedTask;
        }
    }

    private sealed class Applier : IArtifactApplier<BaseColorWorkKey, BaseColorArtifactOutput>
    {
        private readonly PbrMaterialRegistry registry;
        private readonly long regenSessionId;

        public Applier(PbrMaterialRegistry registry, long regenSessionId)
        {
            this.registry = registry;
            this.regenSessionId = regenSessionId;
        }

        public void Apply(in ArtifactApplyContext<BaseColorWorkKey, BaseColorArtifactOutput> context)
        {
            if (!context.Output.HasValue)
            {
                return;
            }

            registry.TryApplyBaseColorRegenResultForTests(regenSessionId, context.Key.CacheKey, context.Output.Value.LinearRgb);
        }
    }

    private static void ExtractAssetFingerprint(IAsset asset, out AssetLocation texture, out string? originPath, out long bytes)
    {
        texture = asset.Location;

        originPath = null;
        try { originPath = asset.Origin?.OriginPath; } catch { originPath = null; }

        bytes = 0;
        try { bytes = asset.Data?.Length ?? 0; } catch { bytes = 0; }
    }

    private static Vector3 DecodeRgb16f(in BaseColorRgb16f rgb16f)
    {
        Half r = BitConverter.Int16BitsToHalf(unchecked((short)rgb16f.R));
        Half g = BitConverter.Int16BitsToHalf(unchecked((short)rgb16f.G));
        Half b = BitConverter.Int16BitsToHalf(unchecked((short)rgb16f.B));
        return new Vector3((float)r, (float)g, (float)b);
    }

    private static BaseColorRgb16f EncodeRgb16f(in Vector3 rgb)
    {
        Half r = (Half)rgb.X;
        Half g = (Half)rgb.Y;
        Half b = (Half)rgb.Z;

        ushort rb = unchecked((ushort)BitConverter.HalfToInt16Bits(r));
        ushort gb = unchecked((ushort)BitConverter.HalfToInt16Bits(g));
        ushort bb = unchecked((ushort)BitConverter.HalfToInt16Bits(b));

        return new BaseColorRgb16f(rb, gb, bb);
    }
}
