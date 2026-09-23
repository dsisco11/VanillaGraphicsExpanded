using System;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.LumOn.Scene.Geometry;
using VanillaGraphicsExpanded.Rendering;
using Vintagestory.API.Client;

namespace VanillaGraphicsExpanded.LumOn.Scene;

/// <summary>Owns irradiance history validity and clears stale lighting before consumers can reuse it.</summary>
internal sealed class LumonSceneIrradianceHistory : IDisposable
{
    private readonly ICoreClientAPI capi;
    private TraceGeometryGpuScene? previousScene;
    private long previousInvalidation = -1;
    private int previousAtlas;
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
        long revision = scene?.InvalidationRevision ?? -1;
        if (ReferenceEquals(previousScene, scene) && previousInvalidation == revision && previousAtlas == atlas.TextureId)
            return true;
        if (reset == null && !GpuComputePipeline.TryCreateFromAssets(capi, Shaders.LumonSceneResetIrradianceComputeShader.Contract.Identity,
            out reset, out _, out var log, preferSpirv: true))
        {
            capi.Logger.Warning("[VGE] Cannot invalidate surface lighting: {0}", log);
            return false;
        }

        // Clear the entire atlas: rays can depend on changed geometry outside their own page's chunk.
        using (reset!.UseScope())
        {
            atlas.BindImageUnit(0, TextureAccess.WriteOnly, layered: true, format: SizedInternalFormat.Rgba16f);
            GL.DispatchCompute((atlas.Width + 7) / 8, (atlas.Height + 7) / 8, atlas.Depth);
        }
        GL.MemoryBarrier(MemoryBarrierFlags.ShaderImageAccessBarrierBit | MemoryBarrierFlags.TextureFetchBarrierBit);
        previousScene = scene;
        previousInvalidation = revision;
        previousAtlas = atlas.TextureId;
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
        previousInvalidation = -1;
        previousAtlas = 0;
    }
    #endregion
}
