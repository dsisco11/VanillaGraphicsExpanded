using System.Linq;
using VanillaGraphicsExpanded.LumOn;
using VanillaGraphicsExpanded.LumOn.WorldProbes.Gpu;
using VanillaGraphicsExpanded.PBR;
using VanillaGraphicsExpanded.Rendering.ShaderCompilation;
using Vintagestory.API.Client;

namespace VanillaGraphicsExpanded.Rendering.Shaders;

/// <summary>Declares mod-owned programs for initial startup and shader reload.</summary>
internal static class VgeShaderPrograms
{
    #region Registration
    /// <summary>Declares programs without loading assets; feature owners explicitly preload required selections.</summary>
    internal static bool RegisterAll(ICoreClientAPI api)
    {
        GpuShaderProgramLibraries.Declare(api, PBR.Clouds.CloudSimpleProgram.Contract.Identity, () => new PBR.Clouds.CloudSimpleProgram());
        GpuShaderProgramLibraries.Declare(api, PBR.Clouds.CloudVolumeProgram.Contract.Identity, () => new PBR.Clouds.CloudVolumeProgram());
        GpuShaderProgramLibraries.Declare(api, PBR.Clouds.CloudTraceProgram.Contract.Identity, () => new PBR.Clouds.CloudTraceProgram());
        GpuShaderProgramLibraries.Declare(api, PBR.Clouds.CloudTemporalProgram.Contract.Identity, () => new PBR.Clouds.CloudTemporalProgram());
        GpuShaderProgramLibraries.Declare(api, PBR.Clouds.CloudResolveProgram.Contract.Identity, () => new PBR.Clouds.CloudResolveProgram());
        GpuShaderProgramLibraries.Declare(api,"pbr_bloom_local_exposure",
            ()=>new PBR.Postprocessing.BloomShaderProgram { LocalExposureEnabled=true },
            new Contracts.ShaderSettings(PBR.Postprocessing.BloomShaderProgram.Contract).With("VGE_LOCAL_EXPOSURE","true"));
        GpuShaderProgramLibraries.Declare(api,"pbr_final_local_exposure",
            ()=>new PBR.Postprocessing.FinalDisplayShaderProgram { LocalExposureEnabled=true },
            new Contracts.ShaderSettings(PBR.Postprocessing.FinalDisplayShaderProgram.Contract).With("VGE_LOCAL_EXPOSURE","true"));
        GpuShaderProgramLibraries.Declare(api, VgeDebugLinesShaderProgram.Contract.Identity, () => new VgeDebugLinesShaderProgram());
        GpuShaderProgramLibraries.Declare(api, DebugView.DebugTextureShaderProgram.Contract.Identity, () => new DebugView.DebugTextureShaderProgram());
        GpuShaderProgramLibraries.Declare(api, DebugView.DebugSurfaceShaderProgram.Contract.Identity, () => new DebugView.DebugSurfaceShaderProgram());
        GpuShaderProgramLibraries.Declare(api, VgeWorldProbeOrbsPointsShaderProgram.Contract.Identity, () => new VgeWorldProbeOrbsPointsShaderProgram());
        GpuShaderProgramLibraries.Declare(api, PBRDirectLightingShaderProgram.Contract.Identity, () => new PBRDirectLightingShaderProgram());
        GpuShaderProgramLibraries.Declare(api, PBR.Atmosphere.AtmosphereSkyShaderProgram.Contract.Identity, () => new PBR.Atmosphere.AtmosphereSkyShaderProgram());
        GpuShaderProgramLibraries.Declare(api, PBR.Atmosphere.Celestial.CelestialGenerationProgram.Contract.Identity, () => new PBR.Atmosphere.Celestial.CelestialGenerationProgram());
        GpuShaderProgramLibraries.Declare(api, PBR.Atmosphere.Celestial.CelestialStarProgram.Contract.Identity, () => new PBR.Atmosphere.Celestial.CelestialStarProgram());
        GpuShaderProgramLibraries.Declare(api, PBR.Atmosphere.Celestial.CelestialGalaxyProgram.Contract.Identity, () => new PBR.Atmosphere.Celestial.CelestialGalaxyProgram());
        GpuShaderProgramLibraries.Declare(api, PBR.Atmosphere.Celestial.MoonProgram.Contract.Identity, () => new PBR.Atmosphere.Celestial.MoonProgram());
        GpuShaderProgramLibraries.Declare(api, PBR.Atmosphere.Celestial.SunProgram.Contract.Identity, () => new PBR.Atmosphere.Celestial.SunProgram());
        GpuShaderProgramLibraries.Declare(api, PBR.Postprocessing.FinalDisplayShaderProgram.Contract.Identity, () => new PBR.Postprocessing.FinalDisplayShaderProgram());
        GpuShaderProgramLibraries.Declare(api, PBR.Postprocessing.BloomShaderProgram.Contract.Identity, () => new PBR.Postprocessing.BloomShaderProgram());
        GpuShaderProgramLibraries.Declare(api, PBR.Postprocessing.LightShaftShaderProgram.Contract.Identity, () => new PBR.Postprocessing.LightShaftShaderProgram());
        GpuShaderProgramLibraries.Declare(api, PBR.Postprocessing.PostLumaShaderProgram.Contract.Identity, () => new PBR.Postprocessing.PostLumaShaderProgram());
        GpuShaderProgramLibraries.Declare(api, PBR.Postprocessing.PostSsaoShaderProgram.Contract.Identity, () => new PBR.Postprocessing.PostSsaoShaderProgram());
        GpuShaderProgramLibraries.Declare(api, PBR.Postprocessing.AmbientOcclusionFilterShaderProgram.Contract.Identity, () => new PBR.Postprocessing.AmbientOcclusionFilterShaderProgram());
        GpuShaderProgramLibraries.Declare(api, PBR.Postprocessing.MinimumChildMaskShaderProgram.Contract.Identity, () => new PBR.Postprocessing.MinimumChildMaskShaderProgram());
        GpuShaderProgramLibraries.Declare(api, PBR.Liquids.LiquidShaderProgram.Contract.Identity, () => new PBR.Liquids.LiquidShaderProgram());
        GpuShaderProgramLibraries.Declare(api, PBR.Liquids.LiquidDepthShaderProgram.Contract.Identity, () => new PBR.Liquids.LiquidDepthShaderProgram());
        GpuShaderProgramLibraries.Declare(api, PBR.Liquids.WaterRefractionReductionShaderProgram.Contract.Identity, () => new PBR.Liquids.WaterRefractionReductionShaderProgram());
        GpuShaderProgramLibraries.Declare(api, PBRCompositeShaderProgram.Contract.Identity, () => new PBRCompositeShaderProgram());
        GpuShaderProgramLibraries.Declare(api, PBRDisplayResolveShaderProgram.Contract.Identity, () => new PBRDisplayResolveShaderProgram());
        GpuShaderProgramLibraries.Declare(api, PBR.SceneColor.SceneColorParticleShaderProgram.Contract.Identity, () => new PBR.SceneColor.SceneColorParticleShaderProgram());
        GpuShaderProgramLibraries.Declare(api, PBR.SceneColor.SceneColorParticleSsaoShaderProgram.Contract.Identity, () => new PBR.SceneColor.SceneColorParticleSsaoShaderProgram());
        GpuShaderProgramLibraries.Declare(api, LumOnWorldProbeClipmapResolveShaderProgram.Contract.Identity, () => new LumOnWorldProbeClipmapResolveShaderProgram());
        GpuShaderProgramLibraries.Declare(api, LumOnWorldProbeRadianceTileResolveShaderProgram.Contract.Identity, () => new LumOnWorldProbeRadianceTileResolveShaderProgram());
        GpuShaderProgramLibraries.Declare(api, LumOnProbeAnchorShaderProgram.Contract.Identity, () => new LumOnProbeAnchorShaderProgram());
        GpuShaderProgramLibraries.Declare(api, LumOnProbeAtlasPisMaskShaderProgram.Contract.Identity, () => new LumOnProbeAtlasPisMaskShaderProgram());
        GpuShaderProgramLibraries.Declare(api, LumOnScreenProbeAtlasTraceShaderProgram.Contract.Identity, () => new LumOnScreenProbeAtlasTraceShaderProgram());
        GpuShaderProgramLibraries.Declare(api, LumOnVelocityShaderProgram.Contract.Identity, () => new LumOnVelocityShaderProgram());
        GpuShaderProgramLibraries.Declare(api, LumOnScreenProbeAtlasTemporalShaderProgram.Contract.Identity, () => new LumOnScreenProbeAtlasTemporalShaderProgram());
        GpuShaderProgramLibraries.Declare(api, LumOnScreenProbeAtlasFilterShaderProgram.Contract.Identity, () => new LumOnScreenProbeAtlasFilterShaderProgram());
        GpuShaderProgramLibraries.Declare(api, LumOnScreenProbeAtlasProjectSh9ShaderProgram.Contract.Identity, () => new LumOnScreenProbeAtlasProjectSh9ShaderProgram());
        GpuShaderProgramLibraries.Declare(api, LumOnProbeSh9GatherShaderProgram.Contract.Identity, () => new LumOnProbeSh9GatherShaderProgram());
        GpuShaderProgramLibraries.Declare(api, LumOnScreenProbeAtlasGatherShaderProgram.Contract.Identity, () => new LumOnScreenProbeAtlasGatherShaderProgram());
        GpuShaderProgramLibraries.Declare(api, LumOnUpsampleShaderProgram.Contract.Identity, () => new LumOnUpsampleShaderProgram());
        GpuShaderProgramLibraries.Declare(api, LumOnCombineShaderProgram.Contract.Identity, () => new LumOnCombineShaderProgram());
        GpuShaderProgramLibraries.Declare(api, PBR.Liquids.LiquidShaderProgram.VolumePassName,
            () => new PBR.Liquids.LiquidShaderProgram { CaptureMode = 3 },
            new Contracts.ShaderSettings(PBR.Liquids.LiquidShaderProgram.Contract).With("VGE_LIQUID_CAPTURE_MODE", "3"));
        GpuShaderProgramLibraries.Declare(api, PBRCompositeShaderProgram.PreOverlayPassName,
            () => new PBRCompositeShaderProgram { PreOverlayOnly = true, LumOnEnabled = false, EnablePbrComposite = false, EnableShortRangeAo = false },
            new Contracts.ShaderSettings(PBRCompositeShaderProgram.Contract).With("VGE_COMPOSITE_PRE_OVERLAY_ONLY", "true")
                .With("VGE_LUMON_ENABLED", "false").With("VGE_LUMON_PBR_COMPOSITE", "false").With("VGE_LUMON_ENABLE_SHORT_RANGE_AO", "false"));
        return LumOnDebugShaderProgramFamily.Register(api);
    }
    #endregion
}
