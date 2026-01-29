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

            // Upload work buffer for GPU consumption.
            nearGpu.RelightWork.ResetAndUpload(work.AsSpan(0, workCount));

            if (!EnsureRelightVoxelPipeline())
            {
                return;
            }

            using (relightVoxelPipeline!.UseScope())
            {
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

                _ = relightVoxelPipeline.TrySetUniform3("vge_occOriginMinCell0", occOriginMinCell0.X, occOriginMinCell0.Y, occOriginMinCell0.Z);
                _ = relightVoxelPipeline.TrySetUniform3("vge_occRing0", occRing0.X, occRing0.Y, occRing0.Z);

                _ = relightVoxelPipeline.TrySetUniform1("vge_occResolution", occResolution);

                int gx = (tileSize + 7) / 8;
                int gy = (tileSize + 7) / 8;
                relightVoxelPipeline.Dispatch(gx, gy, workCount);
            }

            GL.MemoryBarrier(MemoryBarrierFlags.ShaderImageAccessBarrierBit);

            // v1: clear NeedsRelight flags for the pages we just touched (best-effort).
            // Scheduling is MRU-based so pages will continue to accumulate even without the flag.
            for (int i = 0; i < workCount; i++)
            {
                feedback.TryClearNearPageFlagsMip0(workVirtualPages[i], LumonScenePageTableEntryPacking.Flags.NeedsRelight);
            }

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
    }

    private void OnLeaveWorld()
    {
        frameIndex = 0;
        relightVoxelPipeline?.Dispose();
        relightVoxelPipeline = null;
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
