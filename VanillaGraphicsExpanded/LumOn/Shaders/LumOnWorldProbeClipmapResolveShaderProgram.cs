using VanillaGraphicsExpanded.Rendering.Contracts;
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
[ShaderProgram("Contract", "lumon_worldprobe_clipmap_resolve", 1)]
[ShaderStage("Contract", ShaderStageKind.Vertex, "lumon_worldprobe_clipmap_resolve.vsh")]
[ShaderStage("Contract", ShaderStageKind.Fragment, "lumon_worldprobe_clipmap_resolve.fsh")]
public sealed partial class LumOnWorldProbeClipmapResolveShaderProgram : GpuProgram
{

    /// <summary>Uses the immutable declaration owned by this shader class.</summary>
    internal override global::VanillaGraphicsExpanded.Rendering.Contracts.GpuShaderContract ProgramContract => Contract;

    private LumOnWorldProbeResolveParamsUbo? paramsUbo;

    public LumOnWorldProbeClipmapResolveShaderProgram()
    {
        ProgramLayout.RegisterContract(Contract.Stages[1].Bindings);

    }

    private LumOnWorldProbeResolveParamsUbo Params => paramsUbo ??= new LumOnWorldProbeResolveParamsUbo();

    #region Static

    public static void Register(ICoreClientAPI api)
    {
        var instance = new LumOnWorldProbeClipmapResolveShaderProgram
        {
            PassName = Contract.Identity,
            AssetDomain = "vanillagraphicsexpanded"
        };

        global::VanillaGraphicsExpanded.Rendering.Shaders.GpuShaderPrograms.Declare(api, instance);
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
