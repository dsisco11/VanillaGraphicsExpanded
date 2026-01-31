using System;
using System.Buffers;

using OpenTK.Graphics.OpenGL;

using VanillaGraphicsExpanded.Numerics;
using VanillaGraphicsExpanded.PBR;
using VanillaGraphicsExpanded.Rendering;

using Vintagestory.API.Client;
using Vintagestory.API.Common;

namespace VanillaGraphicsExpanded.LumOn.Scene;

/// <summary>
/// Phase 22.9: Per-texel relight compute (v1).
/// Consumes the trace scene (occupancy clipmap) and writes into the surface-cache irradiance atlas with temporal accumulation.
/// </summary>
internal sealed class LumonSceneRelightUpdateRenderer : IRenderer, IDisposable
{
    private const double RenderOrderValue = 0.99986; // after feedback+capture (0.9998)
    private const int RenderRangeValue = 1;

    private readonly ICoreClientAPI capi;
    private readonly VgeConfig config;
    private readonly LumonSceneFeedbackUpdateRenderer feedback;
    private readonly LumonSceneOccupancyClipmapUpdateRenderer occupancy;

    private GpuComputePipeline? relightVoxelPipeline;
    private GpuAtomicCounterBuffer? debugCounters;

    private int lastWorkCount;
    private uint lastDbgRays;
    private uint lastDbgHits;
    private uint lastDbgMisses;
    private uint lastDbgOobStarts;
    private int lastClearedNeedsRelightOk;
    private int lastClearedNeedsRelightFail;
    private VectorInt3 lastOccOriginMinCell0;
    private VectorInt3 lastOccRing0;
    private int lastOccResolution;

    private int frameIndex;

    public double RenderOrder => RenderOrderValue;
    public int RenderRange => RenderRangeValue;

    public LumonSceneRelightUpdateRenderer(
        ICoreClientAPI capi,
        VgeConfig config,
        LumonSceneFeedbackUpdateRenderer feedback,
        LumonSceneOccupancyClipmapUpdateRenderer occupancy)
    {
        this.capi = capi ?? throw new ArgumentNullException(nameof(capi));
        this.config = config ?? throw new ArgumentNullException(nameof(config));
        this.feedback = feedback ?? throw new ArgumentNullException(nameof(feedback));
        this.occupancy = occupancy ?? throw new ArgumentNullException(nameof(occupancy));

        capi.Event.RegisterRenderer(this, EnumRenderStage.Done, "vge_lumonscene_relight");
        capi.Event.LeaveWorld += OnLeaveWorld;
    }

    public void OnRenderFrame(float deltaTime, EnumRenderStage stage)
    {
        if (stage != EnumRenderStage.Done)
        {
            return;
        }

        if (!config.LumOn.Enabled || !config.LumOn.LumonScene.Enabled)
        {
            return;
        }

        var cfg = config.LumOn.LumonScene;

        if (!feedback.TryGetNearDispatchState(
            out var nearPool,
            out var nearGpu,
            out var physicalToVirtual,
            out var pageTableMirrorMip0))
        {
            return;
        }

        var atlases = nearPool.GpuResources;
        if (atlases is null)
        {
            return;
        }

        var occRes = occupancy.Resources;
        if (occRes is null || occRes.OccupancyLevels.Length <= 0)
        {
            return;
        }

        if (!occupancy.TryGetLevel0RuntimeParams(out VectorInt3 occOriginMinCell0, out VectorInt3 occRing0, out int occResolution))
        {
            return;
        }

        lastOccOriginMinCell0 = occOriginMinCell0;
        lastOccRing0 = occRing0;
        lastOccResolution = occResolution;

        int maxPages = cfg.RelightMaxPagesPerFrame;
        if (maxPages <= 0)
        {
            return;
        }

        int tileSize = nearPool.Plan.TileSizeTexels;
        int tilesPerAxis = nearPool.Plan.TilesPerAxis;
        int tilesPerAtlas = nearPool.Plan.TilesPerAtlas;

        // Select pages to relight (prioritize MRU pages as a proxy for "visible/recently requested").
        int wantIds = Math.Min(checked(maxPages * 4), nearPool.PagePool.CapacityPages);
        uint[] mruIds = ArrayPool<uint>.Shared.Rent(Math.Max(1, wantIds));
        LumonSceneRelightWorkGpu[] work = ArrayPool<LumonSceneRelightWorkGpu>.Shared.Rent(Math.Max(1, maxPages));
        int[] workVirtualPages = ArrayPool<int>.Shared.Rent(Math.Max(1, maxPages));

        int workCount = 0;
        int mruWritten = 0;

        try
        {
            mruWritten = nearPool.PagePool.CopyMostRecentlyUsed(mruIds.AsSpan(0, wantIds));
            for (int i = 0; i < mruWritten && workCount < maxPages; i++)
            {
                uint physicalPageId = mruIds[i];
                if (physicalPageId == 0u)
                {
                    continue;
                }

                if (!physicalToVirtual.TryGetValue(physicalPageId, out int vpage))
                {
                    continue;
                }

                if ((uint)vpage >= (uint)pageTableMirrorMip0.Length)
                {
                    continue;
                }

                // v1: patchId is placeholder; use virtualPageIndex as a stable seed.
                uint patchId = (uint)vpage;
                work[workCount++] = new LumonSceneRelightWorkGpu(physicalPageId, chunkSlot: 0u, patchId, virtualPageIndex: (uint)vpage);
                workVirtualPages[workCount - 1] = vpage;
            }

            if (workCount <= 0)
            {
                return;
            }

            lastWorkCount = workCount;

            // Upload work buffer for GPU consumption.
            nearGpu.RelightWork.ResetAndUpload(work.AsSpan(0, workCount));

            if (!EnsureRelightVoxelPipeline())
            {
                return;
            }

            using (relightVoxelPipeline!.UseScope())
            {
                bool dbg = config.Debug.LumOnRuntimeSelfCheckEnabled;
                EnsureDebugCountersCreated();

                nearGpu.RelightWork.Items.BindBase(bindingIndex: 0);

                // Samplers (use layout(binding=...) in GLSL, cached by ProgramLayout).
                _ = relightVoxelPipeline.ProgramLayout.TryBindSamplerTexture("vge_depthAtlas", TextureTarget.Texture2DArray, atlases.DepthAtlasTextureId);
                _ = relightVoxelPipeline.ProgramLayout.TryBindSamplerTexture("vge_materialAtlas", TextureTarget.Texture2DArray, atlases.MaterialAtlasTextureId);

                _ = relightVoxelPipeline.ProgramLayout.TryBindSamplerTexture("vge_occL0", TextureTarget.Texture3D, occRes.OccupancyLevels[0].TextureId);
                _ = relightVoxelPipeline.ProgramLayout.TryBindSamplerTexture("vge_lightColorLut", TextureTarget.Texture2D, occRes.LightColorLut.TextureId);
                _ = relightVoxelPipeline.ProgramLayout.TryBindSamplerTexture("vge_blockLevelScalarLut", TextureTarget.Texture2D, occRes.BlockLevelScalarLut.TextureId);
                _ = relightVoxelPipeline.ProgramLayout.TryBindSamplerTexture("vge_sunLevelScalarLut", TextureTarget.Texture2D, occRes.SunLevelScalarLut.TextureId);

                // Output atlas as layered image (unit derived from shader layout(binding=...)).
                _ = relightVoxelPipeline.ProgramLayout.TryBindImageTexture(
                    imageUniformName: "vge_irradianceAtlas",
                    texture: atlases.IrradianceAtlas,
                    access: TextureAccess.ReadWrite,
                    level: 0,
                    layered: true,
                    layer: 0,
                    formatOverride: SizedInternalFormat.Rgba16f);

                _ = relightVoxelPipeline.TrySetUniform1("vge_tileSizeTexels", (uint)tileSize);
                _ = relightVoxelPipeline.TrySetUniform1("vge_tilesPerAxis", (uint)tilesPerAxis);
                _ = relightVoxelPipeline.TrySetUniform1("vge_tilesPerAtlas", (uint)tilesPerAtlas);
                _ = relightVoxelPipeline.TrySetUniform1("vge_borderTexels", 0u);

                _ = relightVoxelPipeline.TrySetUniform1("vge_frameIndex", frameIndex);
                _ = relightVoxelPipeline.TrySetUniform1("vge_texelsPerPagePerFrame", (uint)Math.Max(0, cfg.RelightTexelsPerPagePerFrame));
                _ = relightVoxelPipeline.TrySetUniform1("vge_raysPerTexel", (uint)Math.Max(0, cfg.RelightRaysPerTexel));
                _ = relightVoxelPipeline.TrySetUniform1("vge_maxDdaSteps", (uint)Math.Max(0, cfg.RelightMaxDdaSteps));

                _ = relightVoxelPipeline.TrySetUniform1("vge_debugCountersEnabled", dbg ? 1u : 0u);

                _ = relightVoxelPipeline.TrySetUniform3("vge_occOriginMinCell0", occOriginMinCell0.X, occOriginMinCell0.Y, occOriginMinCell0.Z);
                _ = relightVoxelPipeline.TrySetUniform3("vge_occRing0", occRing0.X, occRing0.Y, occRing0.Z);

                _ = relightVoxelPipeline.TrySetUniform1("vge_occResolution", occResolution);

                if (debugCounters is not null && debugCounters.IsValid)
                {
                    Span<uint> zero = stackalloc uint[4] { 0u, 0u, 0u, 0u };
                    debugCounters.UploadSubData((ReadOnlySpan<uint>)zero, dstOffsetBytes: 0);
                    debugCounters.BindBase(bindingIndex: 1);
                }

                int gx = (tileSize + 7) / 8;
                int gy = (tileSize + 7) / 8;
                relightVoxelPipeline.Dispatch(gx, gy, workCount);
            }

            GL.MemoryBarrier(MemoryBarrierFlags.ShaderImageAccessBarrierBit);

            if (config.Debug.LumOnRuntimeSelfCheckEnabled && debugCounters is not null && debugCounters.IsValid)
            {
                using var mapped = debugCounters.MapRange<uint>(dstOffsetBytes: 0, elementCount: 4, access: MapBufferAccessMask.MapReadBit);
                if (mapped.IsMapped && mapped.Span.Length >= 4)
                {
                    lastDbgRays = mapped.Span[0];
                    lastDbgHits = mapped.Span[1];
                    lastDbgMisses = mapped.Span[2];
                    lastDbgOobStarts = mapped.Span[3];
                }
            }

            // v1: clear NeedsRelight flags for the pages we just touched (best-effort).
            // Scheduling is MRU-based so pages will continue to accumulate even without the flag.
            int clearOk = 0;
            int clearFail = 0;
            for (int i = 0; i < workCount; i++)
            {
                if (feedback.TryClearNearPageFlagsMip0(workVirtualPages[i], LumonScenePageTableEntryPacking.Flags.NeedsRelight))
                {
                    clearOk++;
                }
                else
                {
                    clearFail++;
                }
            }

            lastClearedNeedsRelightOk = clearOk;
            lastClearedNeedsRelightFail = clearFail;

            frameIndex = unchecked(frameIndex + 1);
        }
        finally
        {
            ArrayPool<uint>.Shared.Return(mruIds, clearArray: false);
            ArrayPool<LumonSceneRelightWorkGpu>.Shared.Return(work, clearArray: false);
            ArrayPool<int>.Shared.Return(workVirtualPages, clearArray: false);
        }
    }

    public void Dispose()
    {
        capi.Event.LeaveWorld -= OnLeaveWorld;

        relightVoxelPipeline?.Dispose();
        relightVoxelPipeline = null;

        debugCounters?.Dispose();
        debugCounters = null;
    }

    private void OnLeaveWorld()
    {
        frameIndex = 0;
        lastWorkCount = 0;
        lastDbgRays = 0;
        lastDbgHits = 0;
        lastDbgMisses = 0;
        lastDbgOobStarts = 0;
        lastClearedNeedsRelightOk = 0;
        lastClearedNeedsRelightFail = 0;
        relightVoxelPipeline?.Dispose();
        relightVoxelPipeline = null;

        debugCounters?.Dispose();
        debugCounters = null;
    }

    private void EnsureDebugCountersCreated()
    {
        if (debugCounters is not null && debugCounters.IsValid)
        {
            return;
        }

        debugCounters?.Dispose();
        debugCounters = GpuAtomicCounterBuffer.Create(BufferUsageHint.DynamicDraw, debugName: "LumOn.LumonScene.Relight.DebugCounters(ACBO)");
        debugCounters.InitializeCounters(counterCount: 4, initialValue: 0);
    }

    internal bool TryGetSelfCheckLine(out string line)
    {
        line = string.Empty;
        if (!config.LumOn.Enabled || !config.LumOn.LumonScene.Enabled)
        {
            return false;
        }

        line =
            $"LSR: pages:{lastWorkCount} clr:{lastClearedNeedsRelightOk}/{lastWorkCount} fail:{lastClearedNeedsRelightFail} " +
            $"rays:{lastDbgRays} hit:{lastDbgHits} miss:{lastDbgMisses} oob0:{lastDbgOobStarts} " +
            $"occ0:({lastOccOriginMinCell0.X},{lastOccOriginMinCell0.Y},{lastOccOriginMinCell0.Z}) r:({lastOccRing0.X},{lastOccRing0.Y},{lastOccRing0.Z}) res:{lastOccResolution}";
        return true;
    }

    private bool EnsureRelightVoxelPipeline()
    {
        if (relightVoxelPipeline is not null && relightVoxelPipeline.IsValid)
        {
            return true;
        }

        relightVoxelPipeline?.Dispose();
        relightVoxelPipeline = null;

        var loc = AssetLocation.Create("shaders/lumonscene_relight_voxel_dda.csh", ShaderImportsSystem.DefaultDomain);
        IAsset? asset = capi.Assets.TryGet(loc, loadAsset: true);
        if (asset is null)
        {
            capi.Logger.Warning("[VGE] Missing LumonScene relight shader asset: {0}", loc);
            return false;
        }

        string src = asset.ToText();

        if (!GpuComputePipeline.TryCompileAndCreateGlslPreprocessed(
            api: capi,
            shaderName: "lumonscene_relight_voxel_dda",
            glslSource: src,
            pipeline: out relightVoxelPipeline,
            sourceCode: out _,
            infoLog: out string infoLog,
            stageExtension: "csh",
            defines: null,
            debugName: "LumOn.LumonScene.RelightVoxelDda",
            log: capi.Logger))
        {
            capi.Logger.Error("[VGE] Failed to compile LumonScene relight compute shader: {0}", infoLog);
            relightVoxelPipeline = null;
            return false;
        }

        return relightVoxelPipeline is not null;
    }
}
