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
            new VgeWorldProbeOrbsPointsShaderProgram(),
            new PBRDirectLightingShaderProgram(),
            new PBRCompositeShaderProgram(),
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
