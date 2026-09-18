using Vintagestory.API.Client;
using Vintagestory.API.MathTools;

using VanillaGraphicsExpanded.Rendering.Shaders;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.LumOn.Shaders;

namespace VanillaGraphicsExpanded.LumOn;

/// <summary>
/// Shader program that resolves CPU-produced world-probe per-probe scalar outputs into clipmap textures.
/// Implementation strategy: render 1px points into an MRT FBO, one point per probe update.
/// </summary>
public sealed class LumOnWorldProbeClipmapResolveShaderProgram : GpuProgram
{
    private LumOnWorldProbeResolveParamsUbo? paramsUbo;

    public LumOnWorldProbeClipmapResolveShaderProgram()
    {
        RegisterUniformBlockBinding(LumOnWorldProbeResolveParamsUbo.BlockName, GpuBindingRegistry.Ubo.Object, required: true);
    }

    private LumOnWorldProbeResolveParamsUbo Params => paramsUbo ??= new LumOnWorldProbeResolveParamsUbo();

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

    public Vec2f AtlasSize
    {
        set
        {
            Params.AtlasSize = value;
            Params.BindTo(this, LumOnWorldProbeResolveParamsUbo.BlockName, $"VGE.{ShaderName}.Params");
        }
    }
}
