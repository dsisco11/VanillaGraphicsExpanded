using System;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.LumOn.Scene.Geometry;
using VanillaGraphicsExpanded.Rendering;
using Vintagestory.API.Client;

namespace VanillaGraphicsExpanded.LumOn.Scene;

/// <summary>Clears incompatible storage while retaining valid lighting through source freshness changes.</summary>
internal sealed class LumonSceneIrradianceHistory : IDisposable
{
    private readonly ICoreClientAPI capi;
    private TraceGeometryGpuScene? previousScene;
    private GpuTexture? previousAtlas;
    private GpuComputePipeline? reset;

    /// <summary>Counts successful history resets for consumers with partial relight schedules.</summary>
    public long Revision { get; private set; }

    #region History synchronization
    /// <summary>Uses the runtime asset provider to load the production reset shader.</summary>
    public LumonSceneIrradianceHistory(ICoreClientAPI capi) => this.capi = capi;

    /// <summary>Clears changed scene or atlas history, leaving the prior identity intact if shader loading fails.</summary>
    public bool TrySynchronize(TraceGeometryGpuScene? scene, GpuTexture atlas, out bool invalidated)
    {
        invalidated = false;
        // Temporary loss of geometry is not permission to erase a retained generation.
        if (scene == null) return false;
        if (ReferenceEquals(previousScene, scene) && ReferenceEquals(previousAtlas, atlas))
            return true;
        if (reset == null && !GpuComputePipeline.TryCreateFromAssets(capi, Shaders.LumonSceneResetIrradianceComputeShader.Contract.Identity,
            out reset, out _, out var log, preferSpirv: true))
        {
            capi.Logger.Warning("[VGE] Cannot invalidate surface lighting: {0}", log);
            return false;
        }

        // Scene replacement changes immutable material identities; atlas replacement is new storage.
        using (reset!.UseScope())
        {
            atlas.BindImageUnit(0, TextureAccess.WriteOnly, layered: true, format: SizedInternalFormat.Rgba16f);
            GL.DispatchCompute((atlas.Width + 7) / 8, (atlas.Height + 7) / 8, atlas.Depth);
        }
        GL.MemoryBarrier(MemoryBarrierFlags.ShaderImageAccessBarrierBit | MemoryBarrierFlags.TextureFetchBarrierBit);
        previousScene = scene;
        previousAtlas = atlas;
        Revision++;
        invalidated = true;
        return true;
    }
    #endregion

    #region Lifetime
    /// <summary>Releases the reset program and forgets the prior scene and atlas identity.</summary>
    public void Dispose()
    {
        reset?.Dispose();
        reset = null;
        previousScene = null;
        previousAtlas = null;
    }
    #endregion
}
