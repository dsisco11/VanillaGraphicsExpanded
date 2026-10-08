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
        GpuProgram[] programs =
        [
            new VgeDebugLinesShaderProgram(),
            new DebugView.DebugTextureShaderProgram(),
            new VgeWorldProbeOrbsPointsShaderProgram(),
            new PBRDirectLightingShaderProgram(),
            new PBR.Atmosphere.AtmosphereSkyShaderProgram(),
            new PBR.CameraExposure.CameraHistogramShaderProgram(),
            new PBR.CameraExposure.CameraAdaptShaderProgram(),
            new PBR.Postprocessing.FinalDisplayShaderProgram(),
            new PBR.Postprocessing.BloomShaderProgram(),
            new PBR.Postprocessing.GodRayShaderProgram(),
            new PBR.Postprocessing.PostLumaShaderProgram(),
            new PBR.Postprocessing.PostSsaoShaderProgram(),
            new PBR.Liquids.LiquidShaderProgram(),
            // Keep both liquid executables resident; changing a compile-time mode replaces an executable.
            new PBR.Liquids.LiquidShaderProgram { PassName = PBR.Liquids.LiquidShaderProgram.VolumePassName, CaptureMode = 3 },
            new PBR.Liquids.LiquidDepthShaderProgram(),
            new PBR.Liquids.WaterRefractionReductionShaderProgram(),
            new PBRCompositeShaderProgram(),
            // Capture precedes current-frame LumOn gathering; retain its environment-only executable.
            new PBRCompositeShaderProgram
            {
                PassName = PBRCompositeShaderProgram.PreOverlayPassName,
                PreOverlayOnly = true,
                LumOnEnabled = false,
                EnablePbrComposite = false,
                EnableShortRangeAo = false
            },
            new PBRDisplayResolveShaderProgram(),
            new PBR.SceneColor.SceneColorParticleShaderProgram(),
            new PBR.SceneColor.SceneColorParticleSsaoShaderProgram(),
            new LumOnWorldProbeClipmapResolveShaderProgram(),
            new LumOnWorldProbeRadianceTileResolveShaderProgram(),
            new LumOnProbeAnchorShaderProgram(),
            new LumOnProbeAtlasPisMaskShaderProgram(),
            new LumOnHzbCopyShaderProgram(),
            new LumOnHzbDownsampleShaderProgram(),
            new LumOnScreenProbeAtlasTraceShaderProgram(),
            new LumOnVelocityShaderProgram(),
            new LumOnScreenProbeAtlasTemporalShaderProgram(),
            new LumOnScreenProbeAtlasFilterShaderProgram(),
            new LumOnScreenProbeAtlasProjectSh9ShaderProgram(),
            new LumOnProbeSh9GatherShaderProgram(),
            new LumOnScreenProbeAtlasGatherShaderProgram(),
            new LumOnUpsampleShaderProgram(),
            new LumOnCombineShaderProgram(),
        ];
        foreach (var program in programs) GpuShaderPrograms.Declare(api, program);
        return LumOnDebugShaderProgramFamily.Register(api);
    }
    #endregion
}
