using System;
using System.Threading;
using System.Threading.Tasks;

using OpenTK.Graphics.OpenGL;

using VanillaGraphicsExpanded.Cache.ArtifactSystem;
using VanillaGraphicsExpanded.PBR.Materials;
using VanillaGraphicsExpanded.PBR.Materials.Cache;
using VanillaGraphicsExpanded.Rendering;

using Vintagestory.API.Client;
using Vintagestory.API.Common;

namespace VanillaGraphicsExpanded.PBR.Materials.ArtifactSystem;

/// <summary>Schedules material computation and publishes only to the concrete atlas lifetime captured at enqueue.</summary>
internal sealed class MaterialParamsAtlasArtifactGenerator
{
    private readonly ICoreClientAPI capi;
    private readonly IMaterialAtlasDiskCache diskCache;
    private readonly MaterialAtlasArtifactBuildTracker tracker;
    private readonly System.Func<int, MaterialAtlasPageTextures?> tryGetPageTextures;

    private readonly ArtifactScheduler<WorkKey, Output> scheduler;

    #region Public API
    #region Lifetime
    /// <summary>Retains computation, cache and atlas lifetime boundaries under the existing scheduler.</summary>
    public MaterialParamsAtlasArtifactGenerator(
        ICoreClientAPI capi,
        IMaterialAtlasDiskCache diskCache,
        MaterialAtlasArtifactBuildTracker tracker,
        System.Func<int, MaterialAtlasPageTextures?> tryGetPageTextures,
        int maxConcurrency = 2,
        int ioCapacity = 1,
        int gpuCapacity = 2)
    {
        this.capi = capi ?? throw new ArgumentNullException(nameof(capi));
        this.diskCache = diskCache ?? throw new ArgumentNullException(nameof(diskCache));
        this.tracker = tracker ?? throw new ArgumentNullException(nameof(tracker));
        _ = tryGetPageTextures ?? throw new ArgumentNullException(nameof(tryGetPageTextures));
        this.tryGetPageTextures = tryGetPageTextures;

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

    /// <summary>Starts the retained computation scheduler.</summary>
    public void Start() => scheduler.Start();

    /// <summary>Stops scheduling without transferring atlas ownership.</summary>
    public void Stop() => scheduler.Stop();

    /// <summary>Withdraws work belonging to the preceding material build session.</summary>
    public void BumpSession() => scheduler.BumpSession();
    #endregion

    #region Diagnostics and completion
    /// <summary>Reports the existing scheduler's current workload.</summary>
    public ArtifactSchedulerStats GetStatsSnapshot() => scheduler.GetStatsSnapshot();

    /// <summary>Waits for scheduled material computation and output to finish.</summary>
    public Task WaitForIdleAsync(CancellationToken cancellationToken = default)
        => scheduler.WaitForIdleAsync(cancellationToken);

    /// <summary>Drains material work on the current owner thread for synchronous builds.</summary>
    public void FinishOnCurrentThread(CancellationToken cancellationToken = default)
        => scheduler.FinishOnCurrentThread(cancellationToken);
    #endregion

    #region Scheduling
    /// <summary>Captures current material metadata before scheduling an atlas job.</summary>
    public bool Enqueue(WorkKey key)
    {
        // Capture the concrete page on the owning thread. Background jobs must neither
        // traverse the live atlas dictionary nor retarget a replacement texture name.
        MaterialAtlasPageTextures? page = tryGetPageTextures(key.AtlasTextureId);
        if (page is null || !page.MaterialParamsTexture.IsValid
            || page.MaterialParamsTexture.TextureId != key.MaterialParamsTextureId) return false;
        key = key with { PublicationPage = page };
        // Override-only RGB jobs still require the mapped material transmission at publication.
        if (key.Definition == null && PbrMaterialRegistry.Instance.TryGetMaterial(key.TargetTexture, out var material))
            key = key with { Definition = material };
        return scheduler.Enqueue(new WorkItem(key));
    }

    /// <summary>Identifies computation inputs and the exact receiving page, independent of later atlas replacements.</summary>
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
        int Priority,
        MaterialAtlasPageTextures? PublicationPage = null);

    /// <summary>Retains computed material data with the same receiving lifetime and source metadata.</summary>
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
        int Priority,
        float Transmission = 0,
        MaterialAtlasPageTextures? PublicationPage = null);

    #endregion
    #endregion

    #region Private
    /// <summary>Adapts immutable material inputs to the scheduler's reservation and priority contract.</summary>
    private sealed class WorkItem : IArtifactWorkItem<WorkKey>
    {
        #region Public API
        /// <summary>Retains the already captured receiving page and computation inputs.</summary>
        public WorkItem(WorkKey key) => Key = key;

        public WorkKey Key { get; }

        public int Priority => Key.Priority;

        public string TypeId => "MaterialParamsAtlas";

        public string DebugLabel => Key.TargetTexture.ToString();

        // Conservative: reserve both disk and GPU because compute may decide to store.
        public ArtifactOutputKinds RequiredOutputKinds => ArtifactOutputKinds.Disk | ArtifactOutputKinds.Gpu;
        #endregion
    }

    /// <summary>Computes material coefficients while preserving the captured publication destination.</summary>
    private sealed class Computer : IArtifactComputer<WorkKey, Output>
    {
        private readonly IMaterialAtlasDiskCache diskCache;
        private readonly MaterialOverrideTextureLoader overrideLoader = new();

        #region Public API
        /// <summary>Retains the optional material cache for procedural and override jobs.</summary>
        public Computer(IMaterialAtlasDiskCache diskCache)
        {
            this.diskCache = diskCache;
        }

        /// <summary>Loads or evaluates a complete tile and carries its exact receiving lifetime to output.</summary>
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
                    Priority: key.Priority,
                    Transmission: key.Definition?.Transmission ?? 0,
                    PublicationPage: key.PublicationPage);

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
                Priority: key.Priority,
                Transmission: key.Definition?.Transmission ?? 0,
                PublicationPage: key.PublicationPage);

            return ValueTask.FromResult(new ArtifactComputeResult<Output>(IsNoop: false, Output: new Optional<Output>(outp), RequiresApply: true));
        }
        #endregion
    }

    /// <summary>Stages admitted GPU data through its captured owner before writing optional disk caches.</summary>
    private sealed class OutputStage : IArtifactOutputStage<WorkKey, Output>
    {
        private readonly IMaterialAtlasDiskCache diskCache;
        #region Public API
        /// <summary>Retains cache output while each job carries its exact publication owner.</summary>
        public OutputStage(IMaterialAtlasDiskCache diskCache)
        {
            this.diskCache = diskCache;
        }

        /// <summary>Rejects retired destinations and unsupported data before reporting publication success.</summary>
        public ValueTask OutputAsync(ArtifactOutputContext<WorkKey, Output> context)
        {
            if (context.Session.CancellationToken.IsCancellationRequested)
            {
                return ValueTask.CompletedTask;
            }

            float[] rgba = MaterialTransmission.Pack(context.Output.RgbTriplets, context.Output.Transmission);
            if (context.Output.PublicationPage?.TryPublishMaterial(context.Output.MaterialParamsTextureId,
                context.Output.Rect, rgba, context.Session.Mode == ArtifactExecutionMode.InlineCurrentThread) != true)
                throw new OperationCanceledException("The material atlas publication was retired.");

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
        #endregion
    }

    /// <summary>Records completion only after the scheduler accepts material output.</summary>
    private sealed class Applier : IArtifactApplier<WorkKey, Output>
    {
        private readonly MaterialAtlasArtifactBuildTracker tracker;

        #region Public API
        /// <summary>Retains the build generation's completion tracker.</summary>
        public Applier(MaterialAtlasArtifactBuildTracker tracker)
        {
            this.tracker = tracker;
        }

        /// <summary>Completes the matching generation without rebinding a retired publication page.</summary>
        public void Apply(in ArtifactApplyContext<WorkKey, Output> context)
        {
            tracker.CompleteOne(context.Key.GenerationId);
        }
        #endregion
    }
    #endregion
}
