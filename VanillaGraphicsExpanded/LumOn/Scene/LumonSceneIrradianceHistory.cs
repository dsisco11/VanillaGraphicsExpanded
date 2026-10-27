using VanillaGraphicsExpanded.Rendering.ShaderCompilation;
using VanillaGraphicsExpanded.Rendering.RHI;
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
    private Shaders.LumonSceneResetIrradianceComputeShader? reset;

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
        if (reset == null)
        {
            if (!ComputeShaderAsset.TryCreateFromAssets(api: capi,
            settings: new global::VanillaGraphicsExpanded.Rendering.Contracts.ShaderSettings(global::VanillaGraphicsExpanded.Rendering.Contracts.GpuShaderContracts.Registry.FindProgram(Shaders.LumonSceneResetIrradianceComputeShader.Contract.Identity)),
            pipeline: out var created,
            infoLog: out var log))
            {
                capi.Logger.Warning("[VGE] Cannot invalidate surface lighting: {0}", log);
                return false;
            }
            reset = new(created!);
        }
        reset.irradianceAtlas = new(atlas, ImageAccess.WriteOnly, Layered: true, Format: TextureFormat.Rgba16f);
        if(!reset.EnsureReady())throw new InvalidOperationException(reset.PreparationLog);
        var dispatchContext=RHICommandContext.Current;
        using var dispatchBoundary=dispatchContext.EnterBoundary(new("SurfaceLighting.Reset",true,compute:[reset.Pipeline]));
        dispatchBoundary.Run(()=>reset.Dispatch((atlas.Width + 7) / 8, (atlas.Height + 7) / 8, atlas.Depth));
        RHICommandContext.Current.Barrier(new(ResourceAccess.Image | ResourceAccess.TextureFetch));
        previousScene = scene;
        previousAtlas = atlas;
        Revision++;
        invalidated = true;
        return true;
    }
    #endregion

    /// <summary>Withdraws prior publication identity while preserving the reset program wrapper and inputs.</summary>
    internal void InvalidateAssets() { previousScene = null; previousAtlas = null; }
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
