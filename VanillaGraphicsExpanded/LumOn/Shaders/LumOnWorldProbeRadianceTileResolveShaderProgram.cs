using Vintagestory.API.Client;
using Vintagestory.API.MathTools;

using VanillaGraphicsExpanded.Rendering.Shaders;

namespace VanillaGraphicsExpanded.LumOn;

/// <summary>
/// Shader program that resolves CPU-produced world-probe radiance tile samples into the radiance atlas.
/// Implementation strategy: render 1px points into the radiance FBO, one point per traced texel.
/// </summary>
public sealed class LumOnWorldProbeRadianceTileResolveShaderProgram : GpuProgram
{
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

    public Vec2f AtlasSize { set => Uniform("atlasSize", value); }
}
