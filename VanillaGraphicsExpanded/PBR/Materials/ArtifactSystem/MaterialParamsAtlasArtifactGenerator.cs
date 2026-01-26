using System;
using System.Threading.Tasks;

using OpenTK.Graphics.OpenGL;

using VanillaGraphicsExpanded.Cache.Artifacts;
using VanillaGraphicsExpanded.PBR.Materials.Cache;
using VanillaGraphicsExpanded.Rendering;

using Vintagestory.API.Client;
using Vintagestory.API.Common;

namespace VanillaGraphicsExpanded.PBR.Materials.Artifacts;

internal sealed class MaterialParamsAtlasArtifactGenerator
{
    private readonly ICoreClientAPI capi;
    private readonly IMaterialAtlasDiskCache diskCache;
    private readonly MaterialAtlasArtifactBuildTracker tracker;

    private readonly ArtifactScheduler<WorkKey, Output> scheduler;

    public MaterialParamsAtlasArtifactGenerator(
        ICoreClientAPI capi,
        IMaterialAtlasDiskCache diskCache,
        MaterialAtlasArtifactBuildTracker tracker,
        int maxConcurrency = 2,
        int ioCapacity = 1,
        int gpuCapacity = 2)
    {
        this.capi = capi ?? throw new ArgumentNullException(nameof(capi));
        this.diskCache = diskCache ?? throw new ArgumentNullException(nameof(diskCache));
        this.tracker = tracker ?? throw new ArgumentNullException(nameof(tracker));

        var diskReservations = new ArtifactReservationPool(Math.Max(0, ioCapacity));
        var gpuReservations = new ArtifactReservationPool(Math.Max(0, gpuCapacity));

        scheduler = new ArtifactScheduler<WorkKey, Output>(
            capi,
            new Computer(diskCache),
            new OutputStage(diskCache),
            new Applier(tracker),
            maxConcurrency: Math.Max(1, maxConcurrency),
            diskReservations: diskReservations,
            gpuReservations: gpuReservations);
    }

    public void Start() => scheduler.Start();

    public void Stop() => scheduler.Stop();

    public void BumpSession() => scheduler.BumpSession();

    public ArtifactSchedulerStats GetStatsSnapshot() => scheduler.GetStatsSnapshot();

    public bool Enqueue(WorkKey key)
    {
        return scheduler.Enqueue(new WorkItem(key));
    }

    internal readonly record struct WorkKey(
        int GenerationId,
        int AtlasTextureId,
        int MaterialParamsTextureId,
        AtlasRect Rect,
        AssetLocation TargetTexture,
        AssetLocation? SourceTexture,
        PbrMaterialDefinition? Definition,
        PbrOverrideScale DefinitionScale,
        bool EnableCache,
        bool HasOverride,
        bool IsOverrideOnly,
        AssetLocation? OverrideTexture,
        PbrOverrideScale OverrideScale,
        string? RuleId,
        AssetLocation? RuleSource,
        AtlasCacheKey BaseCacheKey,
        AtlasCacheKey OverrideCacheKey,
        int Priority);

    internal readonly record struct Output(
        int GenerationId,
        int AtlasTextureId,
        int MaterialParamsTextureId,
        AtlasRect Rect,
        float[] RgbTriplets,
        float[]? BaseRgbTripletsForDisk,
        bool StoreBase,
        AtlasCacheKey BaseCacheKey,
        bool StoreOverride,
        AtlasCacheKey OverrideCacheKey,
        int Priority);

    private sealed class WorkItem : IArtifactWorkItem<WorkKey>
    {
        public WorkItem(WorkKey key) => Key = key;

        public WorkKey Key { get; }

        public int Priority => Key.Priority;

        public string TypeId => "MaterialParamsAtlas";

        public string DebugLabel => Key.TargetTexture.ToString();

        // Conservative: reserve both disk and GPU because compute may decide to store.
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

            // Prefer cached post-override output.
            if (key.EnableCache && key.HasOverride && key.OverrideCacheKey.SchemaVersion != 0
                && diskCache.TryLoadMaterialParamsTile(key.OverrideCacheKey, out float[] cachedOverride))
            {
                var output = new Output(
                    key.GenerationId,
                    key.AtlasTextureId,
                    key.MaterialParamsTextureId,
                    key.Rect,
                    cachedOverride,
                    BaseRgbTripletsForDisk: null,
                    StoreBase: false,
                    BaseCacheKey: default,
                    StoreOverride: false,
                    OverrideCacheKey: default,
                    Priority: key.Priority);

                return ValueTask.FromResult(new ArtifactComputeResult<Output>(IsNoop: false, Output: new Optional<Output>(output), RequiresApply: true));
            }

            float[] rgb;
            bool storeBase = false;

            if (key.EnableCache && !key.IsOverrideOnly && key.BaseCacheKey.SchemaVersion != 0
                && diskCache.TryLoadMaterialParamsTile(key.BaseCacheKey, out float[] cachedBase))
            {
                rgb = cachedBase;
            }
            else if (key.IsOverrideOnly)
            {
                rgb = new float[checked(key.Rect.Width * key.Rect.Height * 3)];
                MaterialAtlasParamsBuilder.FillRgbTriplets(
                    rgb,
                    MaterialAtlasParamsBuilder.DefaultRoughness,
                    MaterialAtlasParamsBuilder.DefaultMetallic,
                    MaterialAtlasParamsBuilder.DefaultEmissive);
            }
            else
            {
                PbrMaterialDefinition def = key.Definition ?? throw new InvalidOperationException("Material definition is required for procedural tiles.");
                rgb = MaterialAtlasParamsBuilder.BuildRgb16fTile(
                    key.SourceTexture ?? key.TargetTexture,
                    def,
                    rectWidth: key.Rect.Width,
                    rectHeight: key.Rect.Height,
                    context.Session.CancellationToken);

                storeBase = key.EnableCache && key.BaseCacheKey.SchemaVersion != 0;
            }

            bool storeOverride = false;
            float[]? baseRgbForDisk = null;

            if (key.HasOverride && key.OverrideTexture is not null)
            {
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
                    if (storeBase)
                    {
                        // Preserve the pre-override base payload for the base cache key.
                        baseRgbForDisk = (float[])rgb.Clone();
                    }

                    MaterialAtlasParamsOverrideApplier.ApplyRgbOverride(
                        atlasRgbTriplets: rgb,
                        atlasWidth: key.Rect.Width,
                        atlasHeight: key.Rect.Height,
                        rectX: 0,
                        rectY: 0,
                        rectWidth: key.Rect.Width,
                        rectHeight: key.Rect.Height,
                        overrideRgba01: rgba01,
                        scale: key.OverrideScale);

                    storeOverride = key.EnableCache && key.OverrideCacheKey.SchemaVersion != 0;
                }
            }

            var outp = new Output(
                key.GenerationId,
                key.AtlasTextureId,
                key.MaterialParamsTextureId,
                key.Rect,
                rgb,
                BaseRgbTripletsForDisk: baseRgbForDisk,
                StoreBase: storeBase,
                BaseCacheKey: key.BaseCacheKey,
                StoreOverride: storeOverride,
                OverrideCacheKey: key.OverrideCacheKey,
                Priority: key.Priority);

            return ValueTask.FromResult(new ArtifactComputeResult<Output>(IsNoop: false, Output: new Optional<Output>(outp), RequiresApply: true));
        }
    }

    private sealed class OutputStage : IArtifactOutputStage<WorkKey, Output>
    {
        private readonly IMaterialAtlasDiskCache diskCache;

        public OutputStage(IMaterialAtlasDiskCache diskCache)
        {
            this.diskCache = diskCache;
        }

        public ValueTask OutputAsync(ArtifactOutputContext<WorkKey, Output> context)
        {
            if (context.Session.CancellationToken.IsCancellationRequested)
            {
                return ValueTask.CompletedTask;
            }

            _ = TextureStreamingSystem.StageCopy(
                textureId: context.Output.MaterialParamsTextureId,
                target: TextureUploadTarget.For2D(),
                region: TextureUploadRegion.For2D(context.Output.Rect.X, context.Output.Rect.Y, context.Output.Rect.Width, context.Output.Rect.Height),
                pixelFormat: PixelFormat.Rgb,
                pixelType: PixelType.Float,
                data: context.Output.RgbTriplets,
                priority: TextureUploadPriority.Normal,
                unpackAlignment: 4);

            // Disk cache writes (best-effort; runs off-thread).
            if (context.Output.StoreBase && context.Output.BaseCacheKey.SchemaVersion != 0)
            {
                float[] payload = context.Output.BaseRgbTripletsForDisk ?? context.Output.RgbTriplets;
                diskCache.StoreMaterialParamsTile(context.Output.BaseCacheKey, context.Output.Rect.Width, context.Output.Rect.Height, payload);
            }

            if (context.Output.StoreOverride && context.Output.OverrideCacheKey.SchemaVersion != 0)
            {
                diskCache.StoreMaterialParamsTile(context.Output.OverrideCacheKey, context.Output.Rect.Width, context.Output.Rect.Height, context.Output.RgbTriplets);
            }

            return ValueTask.CompletedTask;
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
