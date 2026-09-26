using System.Linq;
using VanillaGraphicsExpanded.LumOn;
using VanillaGraphicsExpanded.LumOn.WorldProbes.Gpu;
using VanillaGraphicsExpanded.PBR;
using VanillaGraphicsExpanded.Rendering.ShaderCompilation;
using Vintagestory.API.Client;

namespace VanillaGraphicsExpanded.Rendering.Shaders;

/// <summary>Registers completed mod-owned programs for initial startup and shader reload.</summary>
internal static class VgeShaderPrograms
{
    #region Registration
    /// <summary>Submits independent programs before waiting, publishing only successfully prepared owners.</summary>
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
        foreach (var program in programs)
        {
            program.PassName = program.ProgramContract.Identity;
            program.AssetDomain = ShaderImportsSystem.DefaultDomain;
            program.Initialize(api);
        }
        using var batch = new ShaderLinkBatch(api.Assets, ShaderImportsSystem.DefaultDomain,
            programs.Select(program => program.RequestedSettings));
        bool success = true;
        foreach (var program in programs)
        {
            if (program.CompileAndLink())
                api.Shader.RegisterMemoryShaderProgram(program.PassName, program);
            else
            {
                // Failed initial candidates have no executable; engine stage placeholders are not GL objects.
                program.VertexShader = null; program.FragmentShader = null; program.GeometryShader = null;
                program.Dispose();
                success = false;
            }
        }
        success &= LumOnDebugShaderProgramFamily.Register(api);
        return success;
    }
    #endregion
}