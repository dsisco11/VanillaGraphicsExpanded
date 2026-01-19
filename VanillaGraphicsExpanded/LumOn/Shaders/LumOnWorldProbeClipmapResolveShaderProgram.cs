using Vintagestory.API.Client;
using Vintagestory.API.MathTools;

using VanillaGraphicsExpanded.Rendering.Shaders;

namespace VanillaGraphicsExpanded.LumOn;

/// <summary>
/// Shader program that resolves CPU-produced world-probe coefficients into per-level clipmap textures.
/// Implementation strategy: render 1px points into an MRT FBO, one point per probe update.
/// </summary>
public sealed class LumOnWorldProbeClipmapResolveShaderProgram : VgeShaderProgram
{
    #region Static

    public static void Register(ICoreClientAPI api)
    {
        var instance = new LumOnWorldProbeClipmapResolveShaderProgram
        {
            PassName = "lumon_worldprobe_clipmap_resolve",
            AssetDomain = "vanillagraphicsexpanded"
        };

        instance.Initialize(api);
        instance.CompileAndLink();
        api.Shader.RegisterMemoryShaderProgram(instance.PassName, instance);
    }

    #endregion

    public Vec2f AtlasSize { set => Uniform("atlasSize", value); }
}
