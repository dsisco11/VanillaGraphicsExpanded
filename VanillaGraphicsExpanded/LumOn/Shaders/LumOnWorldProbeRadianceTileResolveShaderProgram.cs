using Vintagestory.API.Client;
using Vintagestory.API.MathTools;

using VanillaGraphicsExpanded.Rendering.Shaders;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.LumOn.Shaders;

namespace VanillaGraphicsExpanded.LumOn;

/// <summary>
/// Shader program that resolves CPU-produced world-probe radiance tile samples into the radiance atlas.
/// Implementation strategy: render 1px points into the radiance FBO, one point per traced texel.
/// </summary>
public sealed class LumOnWorldProbeRadianceTileResolveShaderProgram : GpuProgram
{
    private LumOnWorldProbeResolveParamsUbo? paramsUbo;

    public LumOnWorldProbeRadianceTileResolveShaderProgram()
    {
        ProgramLayout.RegisterContract(global::VanillaGraphicsExpanded.Rendering.Contracts.GpuShaderContracts.Create("lumon_worldprobe_radiance_tile_resolve"));

    }

    private LumOnWorldProbeResolveParamsUbo Params => paramsUbo ??= new LumOnWorldProbeResolveParamsUbo();

    #region Static

    public static void Register(ICoreClientAPI api)
    {
        var instance = new LumOnWorldProbeRadianceTileResolveShaderProgram
        {
            PassName = "lumon_worldprobe_radiance_tile_resolve",
            AssetDomain = "vanillagraphicsexpanded"
        };

        instance.Initialize(api);
        instance.CompileAndLink();
        api.Shader.RegisterMemoryShaderProgram(instance.PassName, instance);
    }

    #endregion

    public Vec2f AtlasSize
    {
        set
        {
            Params.AtlasSize = value;
            Params.BindTo(this, LumOnWorldProbeResolveParamsUbo.BlockName, $"VGE.{ShaderName}.Params");
        }
    }
}
