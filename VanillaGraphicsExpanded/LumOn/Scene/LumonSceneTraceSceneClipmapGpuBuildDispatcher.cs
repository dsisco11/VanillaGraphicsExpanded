using System;
using System.Buffers;

using OpenTK.Graphics.OpenGL;

using VanillaGraphicsExpanded.Numerics;
using VanillaGraphicsExpanded.PBR;
using VanillaGraphicsExpanded.LumOn.Scene.Shaders;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Rendering.Profiling;

using Vintagestory.API.Client;
using Vintagestory.API.Common;

namespace VanillaGraphicsExpanded.LumOn.Scene;

/// <summary>
/// Phase 23.4: GL 4.3 compute dispatch that scatters packed 32^3 region payloads into the ring-buffered TraceScene clipmap.
/// </summary>
internal sealed class LumonSceneTraceSceneClipmapGpuBuildDispatcher : IDisposable
{
    private readonly ICoreClientAPI capi;

    private LumonSceneTraceSceneRegionToClipmapComputeShader? shader;

    private readonly LumonSceneTraceSceneRegionUploadGpuResources staging;

    public LumonSceneTraceSceneClipmapGpuBuildDispatcher(ICoreClientAPI capi, int maxRegionUpdatesPerBatch = 16)
    {
        this.capi = capi ?? throw new ArgumentNullException(nameof(capi));
        staging = new LumonSceneTraceSceneRegionUploadGpuResources(maxRegionUpdatesPerBatch);
    }

    public void Dispose()
    {
        shader?.Dispose();
        shader = null;
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

        if (!EnsureShader())
        {
            return 0;
        }

        int count = staging.UploadBatch(regionCoords, regionPayloads, levelMask: levelMask, versionOrPad: 0);
        if (count <= 0)
        {
            return 0;
        }

        using var gpuScope = GlGpuProfiler.Instance.Scope("TraceScene.RegionToClipmap");

        using (shader!.UseScope())
        {
            // Bind staging SSBOs + counter.
            staging.BindForCompute();

            // Bind destination images: OccupancyLevels[i] -> image unit i.
            int max = Math.Min(8, resources.OccupancyLevels.Length);
            for (int i = 0; i < max; i++)
            {
                shader.BindOccLevelImage(i, resources.OccupancyLevels[i], access: TextureAccess.WriteOnly);
            }

            shader.Levels = levels;
            shader.Resolution = resolution;
            shader.RegionUpdateCount = (uint)count;

            // Upload origin/ring arrays (best-effort; unused elements are ignored).
            for (int i = 0; i < Math.Min(8, levels); i++)
            {
                VectorInt3 o = i < originMinCellByLevel.Length ? originMinCellByLevel[i] : default;
                VectorInt3 r = i < ringByLevel.Length ? ringByLevel[i] : default;

                shader.SetOriginMinCell(i, o.X, o.Y, o.Z);
                shader.SetRing(i, r.X, r.Y, r.Z);
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

    private bool EnsureShader()
    {
        if (shader is not null && shader.IsValid)
        {
            return true;
        }

        shader?.Dispose();
        shader = null;

        if (!LumonSceneTraceSceneRegionToClipmapComputeShader.TryCreate(
            api: capi,
            shader: out shader,
            infoLog: out string infoLog,
            preferSpirv: true,
            debugName: "LumOn.TraceScene.RegionToClipmap"))
        {
            capi.Logger.Error("[VGE] Failed to compile TraceScene region->clipmap compute shader: {0}", infoLog);
            shader = null;
            return false;
        }

        return shader is not null;
    }
}
