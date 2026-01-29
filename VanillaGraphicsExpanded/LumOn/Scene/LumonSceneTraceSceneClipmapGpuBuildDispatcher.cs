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
/// Phase 23.4: GL 4.3 compute dispatch that scatters packed 32^3 region payloads into the ring-buffered TraceScene clipmap.
/// </summary>
internal sealed class LumonSceneTraceSceneClipmapGpuBuildDispatcher : IDisposable
{
    private readonly ICoreClientAPI capi;

    private GpuComputePipeline? pipeline;

    private readonly LumonSceneTraceSceneRegionUploadGpuResources staging;

    public LumonSceneTraceSceneClipmapGpuBuildDispatcher(ICoreClientAPI capi, int maxRegionUpdatesPerBatch = 16)
    {
        this.capi = capi ?? throw new ArgumentNullException(nameof(capi));
        staging = new LumonSceneTraceSceneRegionUploadGpuResources(maxRegionUpdatesPerBatch);
    }

    public void Dispose()
    {
        pipeline?.Dispose();
        pipeline = null;
        staging.Dispose();
    }

    /// <summary>
    /// Uploads the provided region payloads into staging SSBOs, then dispatches the region->clipmap compute shader.
    /// Must be called on the render thread (GL context required).
    /// </summary>
    public int UploadAndDispatchBatch(
        LumonSceneOccupancyClipmapGpuResources resources,
        ReadOnlySpan<VectorInt3> regionCoords,
        ReadOnlySpan<ReadOnlyMemory<uint>> regionPayloads,
        int levels,
        uint levelMask,
        ReadOnlySpan<VectorInt3> originMinCellByLevel,
        ReadOnlySpan<VectorInt3> ringByLevel,
        int resolution)
    {
        if (resources is null) throw new ArgumentNullException(nameof(resources));

        levels = Math.Clamp(levels, 0, Math.Min(8, resources.Levels));
        if (levels <= 0 || resolution <= 0)
        {
            return 0;
        }

        if (!EnsurePipeline())
        {
            return 0;
        }

        int count = staging.UploadBatch(regionCoords, regionPayloads, levelMask: levelMask, versionOrPad: 0);
        if (count <= 0)
        {
            return 0;
        }

        using (pipeline!.UseScope())
        {
            // Bind staging SSBOs + counter.
            staging.BindForCompute();

            // Bind destination images: OccupancyLevels[i] -> image unit i.
            int max = Math.Min(8, resources.OccupancyLevels.Length);
            for (int i = 0; i < max; i++)
            {
                resources.OccupancyLevels[i].BindImageUnit(
                    unit: i,
                    access: TextureAccess.WriteOnly,
                    level: 0,
                    layered: false,
                    layer: 0,
                    format: SizedInternalFormat.R32ui);
            }

            _ = pipeline.TrySetUniform1("vge_levels", levels);
            _ = pipeline.TrySetUniform1("vge_resolution", resolution);

            // Upload origin/ring arrays (best-effort; unused elements are ignored).
            for (int i = 0; i < Math.Min(8, levels); i++)
            {
                VectorInt3 o = i < originMinCellByLevel.Length ? originMinCellByLevel[i] : default;
                VectorInt3 r = i < ringByLevel.Length ? ringByLevel[i] : default;

                _ = pipeline.TrySetUniform3($"vge_originMinCell[{i}]", o.X, o.Y, o.Z);
                _ = pipeline.TrySetUniform3($"vge_ring[{i}]", r.X, r.Y, r.Z);
            }

            // Dispatch:
            // - Each region is 32^3 cells.
            // - local_size is 8^3 => 4x4x4 workgroups per region.
            // - We pack regionIndex into gl_WorkGroupID.z: groupsZ = regionCount * 4.
            const int groupsPerRegionXY = 4;
            const int groupsPerRegionZ = 4;

            GL.DispatchCompute(groupsPerRegionXY, groupsPerRegionXY, checked(count * groupsPerRegionZ));

            // Ensure image writes are visible to later sampling/tracing.
            GL.MemoryBarrier(MemoryBarrierFlags.ShaderImageAccessBarrierBit | MemoryBarrierFlags.TextureFetchBarrierBit);
        }

        return count;
    }

    private bool EnsurePipeline()
    {
        if (pipeline is not null && pipeline.IsValid)
        {
            return true;
        }

        pipeline?.Dispose();
        pipeline = null;

        var loc = AssetLocation.Create("shaders/lumonscene_trace_scene_region_to_clipmap.csh", ShaderImportsSystem.DefaultDomain);
        IAsset? asset = capi.Assets.TryGet(loc, loadAsset: true);
        if (asset is null)
        {
            capi.Logger.Warning("[VGE] Missing TraceScene region->clipmap compute shader asset: {0}", loc);
            return false;
        }

        string src = asset.ToText();

        if (!GpuComputePipeline.TryCompileAndCreateGlslPreprocessed(
            api: capi,
            shaderName: "lumonscene_trace_scene_region_to_clipmap",
            glslSource: src,
            pipeline: out pipeline,
            sourceCode: out _,
            infoLog: out string infoLog,
            stageExtension: "csh",
            defines: null,
            debugName: "LumOn.TraceScene.RegionToClipmap",
            log: capi.Logger))
        {
            capi.Logger.Error("[VGE] Failed to compile TraceScene region->clipmap compute shader: {0}", infoLog);
            pipeline = null;
            return false;
        }

        return pipeline is not null;
    }
}
