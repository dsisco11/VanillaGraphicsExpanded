using System;
using HarmonyLib;
using VanillaGraphicsExpanded.PBR.Liquids;
using VanillaGraphicsExpanded.PBR.Materials;
using VanillaGraphicsExpanded.PBR.Shadows;
using VanillaGraphicsExpanded.Rendering.Shaders;
using VanillaGraphicsExpanded.Rendering.Shadows.Configuration;
using VanillaGraphicsExpanded.Rendering.Shadows.Pages;
using Vintagestory.API.Client;
using Vintagestory.Client.NoObf;

namespace VanillaGraphicsExpanded.Rendering.Integration.Shadows;

/// <summary>Composes the configured production frame from existing native world, terrain, entity and material owners.</summary>
internal static class NativeShadowFrameFactory
{
    private static readonly AccessTools.FieldRef<ClientMain,FrustumCulling> Culler=
        AccessTools.FieldRefAccess<ClientMain,FrustumCulling>("frustumCuller");

    #region Public API
    /// <summary>Creates a full fixed allocation only after the previous generation has drained through the renderer's fence.</summary>
    internal static ShadowFrameInstance Create(ICoreClientAPI api,ClientMain game,NativeShadowWorldAdapter world,
        ShadowSettings settings,Func<double> clock)
    {
        if(!ReferenceEquals(api.World,game) || !LiquidMeshSource.TryGet(api,out var terrain))
            throw new InvalidOperationException("Current native terrain owner is unavailable.");
        _=ShadowResourceLayout.Resolve(settings);
        // The completed consumer executables validate actual reflected integer
        // sampler/storage use against the existing capability owner before this
        // generation can publish surface readiness.
        var deferred=GpuShaderProgramLibraries.Get<PBR.PBRDirectLightingShaderProgram>(api,"pbr_direct_lighting");
        var liquid=GpuShaderProgramLibraries.Get<LiquidShaderProgram>(api,"pbr_liquid");
        var boundary=GpuShaderProgramLibraries.Get<LiquidShaderProgram>(api,LiquidShaderProgram.VolumePassName);
        if(deferred?.EnsureReady()!=true || liquid?.EnsureReady()!=true || boundary?.EnsureReady()!=true ||
            !SurfaceShadowPrograms.HasOwned(deferred) || !SurfaceShadowPrograms.HasOwned(liquid) || !SurfaceShadowPrograms.HasOwned(boundary))
            throw new InvalidOperationException("The complete surface shadow consumer interface could not be prepared.");
        NativeShadowFrameResources? resources=null;ShadowPagePool? pages=null;NativeShadowFrameGate? gate=null;
        NativeShadowFrameSource? source=null;
        try
        {
            resources=new(api,game,settings,NativeShadowTerrainFields.Platform(terrain.Renderer).UseSSBOs);
            pages=ShadowPagePool.Create(api,settings.Pages);
            gate=new(api,settings,pages);
            source=new(api,terrain.Renderer,Culler(game),world,resources.Preparation,MaterialAtlasSystem.Instance,
                api.Render.ShaderUniforms,resources.Changes,new NativeShadowSurfaceReceivers(api),settings,resources.Waving,
                clock,resources.Terrain,resources.Entities,resources.Items,gate.Scheduler);
            return new(source,gate,pages,resources);
        }
        catch
        {
            RenderingCleanup.Run(()=>gate?.Dispose(),()=>source?.Dispose(),()=>pages?.Dispose(),()=>resources?.Dispose());
            throw;
        }
    }
    #endregion
}
