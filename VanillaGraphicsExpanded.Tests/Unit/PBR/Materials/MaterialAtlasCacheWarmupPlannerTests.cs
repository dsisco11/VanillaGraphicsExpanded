using System;
using System.Collections.Generic;

using VanillaGraphicsExpanded.PBR.Materials;
using VanillaGraphicsExpanded.PBR.Materials.Async;
using VanillaGraphicsExpanded.PBR.Materials.Cache;

using Vintagestory.API.Common;

namespace VanillaGraphicsExpanded.Tests.Unit.PBR.Materials;

[Trait("Category", "Unit")]
public sealed class MaterialAtlasCacheWarmupPlannerTests
{
    private sealed class FakeDiskCache : IMaterialAtlasDiskCache
    {
        private readonly HashSet<AtlasCacheKey> materialKeys = new();
        private readonly HashSet<AtlasCacheKey> normalDepthKeys = new();

        private readonly Dictionary<AtlasCacheKey, float[]?> materialPayloads = new();
        private readonly Dictionary<AtlasCacheKey, float[]?> normalDepthPayloads = new();

        public void Clear()
        {
            materialKeys.Clear();
            normalDepthKeys.Clear();
            materialPayloads.Clear();
            normalDepthPayloads.Clear();
        }

        public MaterialAtlasDiskCacheStats GetStatsSnapshot()
            => new(
                MaterialParams: default,
                NormalDepth: default,
                TotalEntries: materialKeys.Count + normalDepthKeys.Count,
                TotalBytes: 0,
                EvictedEntries: 0,
                EvictedBytes: 0);

        public bool HasMaterialParamsTile(AtlasCacheKey key) => materialKeys.Contains(key);

        public bool TryLoadMaterialParamsTile(AtlasCacheKey key, out float[] rgbTriplets)
        {
            if (!materialKeys.Contains(key) || !materialPayloads.TryGetValue(key, out float[]? payload) || payload is null)
            {
                rgbTriplets = null!;
                return false;
            }

            rgbTriplets = payload;
            return true;
        }

        public void StoreMaterialParamsTile(AtlasCacheKey key, int width, int height, float[] rgbTriplets)
        {
            materialKeys.Add(key);
            materialPayloads[key] = rgbTriplets;
        }

        public bool HasNormalDepthTile(AtlasCacheKey key) => normalDepthKeys.Contains(key);

        public bool TryLoadNormalDepthTile(AtlasCacheKey key, out float[] rgbaQuads)
        {
            if (!normalDepthKeys.Contains(key) || !normalDepthPayloads.TryGetValue(key, out float[]? payload) || payload is null)
            {
                rgbaQuads = null!;
                return false;
            }

            rgbaQuads = payload;
            return true;
        }

        public void StoreNormalDepthTile(AtlasCacheKey key, int width, int height, float[] rgbaQuads)
        {
            normalDepthKeys.Add(key);
            normalDepthPayloads[key] = rgbaQuads;
        }
    }

    [Fact]
    public void WarmupPlan_EnqueuesOnlyCacheHits_ForOverrideOnlyAndNormalDepthBake()
    {
        var diskCache = new FakeDiskCache();
        var keyBuilder = new MaterialAtlasCacheKeyBuilder();
        var planner = new MaterialAtlasCacheWarmupPlanner(diskCache, keyBuilder);

        var snapshot = new AtlasSnapshot(
            Pages: Array.Empty<AtlasSnapshot.AtlasPage>(),
            Positions: Array.Empty<Vintagestory.API.Client.TextureAtlasPosition?>(),
            ReloadIteration: 1,
            NonNullPositionCount: 1);

        var materialPlan = new AtlasBuildPlan(
            Snapshot: snapshot,
            Pages: Array.Empty<AtlasBuildPlan.AtlasPagePlan>(),
            MaterialParamsTiles: Array.Empty<AtlasBuildPlan.MaterialParamsTileJob>(),
            MaterialParamsOverrides:
            [
                new AtlasBuildPlan.MaterialParamsOverrideJob(
                    AtlasTextureId: 123,
                    Rect: new AtlasRect(0, 0, 8, 8),
                    TargetTexture: new AssetLocation("game", "textures/block/a.png"),
                    OverrideTexture: new AssetLocation("game", "textures/block/a_override.png"),
                    RuleId: "rule-a",
                    RuleSource: new AssetLocation("game", "rules/a.json"),
                    Scale: new PbrOverrideScale(Roughness: 1f, Metallic: 1f, Emissive: 1f, Normal: 1f, Depth: 1f),
                    Priority: 5)
            ],
            NormalDepthTiles: Array.Empty<AtlasBuildPlan.NormalDepthTileJob>(),
            NormalDepthOverrides: Array.Empty<AtlasBuildPlan.NormalDepthOverrideJob>(),
            Stats: default);

        var normalDepthPlan = new MaterialAtlasNormalDepthBuildPlan(
            Snapshot: snapshot,
            Pages:
            [
                new MaterialAtlasNormalDepthBuildPlan.PagePlan(AtlasTextureId: 456, Width: 64, Height: 64)
            ],
            BakeJobs:
            [
                new MaterialAtlasNormalDepthBuildPlan.BakeJob(
                    AtlasTextureId: 456,
                    Rect: new AtlasRect(0, 0, 8, 8),
                    NormalScale: 1f,
                    DepthScale: 1f,
                    SourceTexture: new AssetLocation("game", "textures/block/b.png"),
                    Priority: 7)
            ],
            OverrideJobs: Array.Empty<MaterialAtlasNormalDepthBuildPlan.OverrideJob>(),
            PlanStats: default);

        var inputs = new MaterialAtlasCacheKeyInputs(SchemaVersion: 1, StablePrefix: "schema=1|test");

        AtlasCacheKey matKey = keyBuilder.BuildMaterialParamsOverrideTileKey(
            inputs,
            atlasTextureId: 123,
            rect: new AtlasRect(0, 0, 8, 8),
            targetTexture: new AssetLocation("game", "textures/block/a.png"),
            overrideTexture: new AssetLocation("game", "textures/block/a_override.png"),
            overrideScale: new PbrOverrideScale(Roughness: 1f, Metallic: 1f, Emissive: 1f, Normal: 1f, Depth: 1f),
            ruleId: "rule-a",
            ruleSource: new AssetLocation("game", "rules/a.json"));

        AtlasCacheKey ndKey = keyBuilder.BuildNormalDepthTileKey(
            inputs,
            atlasTextureId: 456,
            rect: new AtlasRect(0, 0, 8, 8),
            texture: new AssetLocation("game", "textures/block/b.png"),
            normalScale: 1f,
            depthScale: 1f);

        diskCache.StoreMaterialParamsTile(matKey, width: 8, height: 8, rgbTriplets: [0.1f, 0.2f, 0.3f]);
        diskCache.StoreNormalDepthTile(ndKey, width: 8, height: 8, rgbaQuads: [0.1f, 0.2f, 0.3f, 0.4f]);

        MaterialAtlasCacheWarmupPlan warmup = planner.CreatePlan(
            generationId: 1,
            materialParamsPlan: materialPlan,
            normalDepthPlan: normalDepthPlan,
            cacheInputs: inputs,
            enableCache: true);

        Assert.Equal(1, warmup.MaterialParamsPlanned);
        Assert.Equal(1, warmup.NormalDepthPlanned);

        var mpJob = Assert.Single(warmup.MaterialParamsCpuJobs);
        MaterialAtlasParamsGpuTileUpload mpUpload = mpJob.Execute(TestContext.Current.CancellationToken);
        Assert.False(mpUpload.SkipUpload);

        var ndJob = Assert.Single(warmup.NormalDepthCpuJobs);
        MaterialAtlasNormalDepthGpuJob ndUpload = ndJob.Execute(TestContext.Current.CancellationToken);
        Assert.Equal(MaterialAtlasNormalDepthGpuJob.Kind.UploadCached, ndUpload.JobKind);
    }

    [Fact]
    public void WarmupPlan_DoesNotEnqueueCacheMisses()
    {
        var diskCache = new FakeDiskCache();
        var keyBuilder = new MaterialAtlasCacheKeyBuilder();
        var planner = new MaterialAtlasCacheWarmupPlanner(diskCache, keyBuilder);

        var snapshot = new AtlasSnapshot(
            Pages: Array.Empty<AtlasSnapshot.AtlasPage>(),
            Positions: Array.Empty<Vintagestory.API.Client.TextureAtlasPosition?>(),
            ReloadIteration: 1,
            NonNullPositionCount: 1);

        var materialPlan = new AtlasBuildPlan(
            Snapshot: snapshot,
            Pages: Array.Empty<AtlasBuildPlan.AtlasPagePlan>(),
            MaterialParamsTiles: Array.Empty<AtlasBuildPlan.MaterialParamsTileJob>(),
            MaterialParamsOverrides:
            [
                new AtlasBuildPlan.MaterialParamsOverrideJob(
                    AtlasTextureId: 1,
                    Rect: new AtlasRect(0, 0, 8, 8),
                    TargetTexture: new AssetLocation("game", "textures/block/a.png"),
                    OverrideTexture: new AssetLocation("game", "textures/block/a_override.png"),
                    RuleId: null,
                    RuleSource: null,
                    Scale: new PbrOverrideScale(Roughness: 1f, Metallic: 1f, Emissive: 1f, Normal: 1f, Depth: 1f),
                    Priority: 0)
            ],
            NormalDepthTiles: Array.Empty<AtlasBuildPlan.NormalDepthTileJob>(),
            NormalDepthOverrides: Array.Empty<AtlasBuildPlan.NormalDepthOverrideJob>(),
            Stats: default);

        var inputs = new MaterialAtlasCacheKeyInputs(SchemaVersion: 1, StablePrefix: "schema=1|test");

        MaterialAtlasCacheWarmupPlan warmup = planner.CreatePlan(
            generationId: 1,
            materialParamsPlan: materialPlan,
            normalDepthPlan: null,
            cacheInputs: inputs,
            enableCache: true);

        Assert.Equal(0, warmup.MaterialParamsPlanned);
        Assert.Equal(0, warmup.NormalDepthPlanned);
        Assert.Empty(warmup.MaterialParamsCpuJobs);
        Assert.Empty(warmup.NormalDepthCpuJobs);
    }
}
