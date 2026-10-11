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
public sealed partial class LumOnWorldProbeClipmapResolveShaderProgram : GpuProgram, ILumOnWorldProbeClipmapResolveShaderProgramBindings
{


    /// <summary>Uses the immutable declaration owned by this shader class.</summary>
    internal override GpuShaderContract ProgramContract => Contract;

    private LumOnWorldProbeResolveParamsUbo? paramsUbo;

    public LumOnWorldProbeClipmapResolveShaderProgram()
    {
        ProgramLayout.RegisterContract(Contract.Stages[1].Bindings);

    }

    /// <summary>Exposes retained parameters with an owner mutation guard.</summary>
    private LumOnWorldProbeResolveParamsUbo Params
    {
        get
        {
            var parameters = paramsUbo ??= OwnUniformBuffer(new LumOnWorldProbeResolveParamsUbo());
            return parameters;
        }
    }

    #region Static

    public static void Register(ICoreClientAPI api)
    {
        GpuShaderProgramLibraries.Declare(api, Contract.Identity, () => new LumOnWorldProbeClipmapResolveShaderProgram
        {
            PassName = Contract.Identity,
            AssetDomain = "vanillagraphicsexpanded"
        });

    }

    #endregion

    public Vec2f AtlasSize
    {
        set
        {
            Params.AtlasSize = value;
        }
    }
    #region Binding sources
    /// <summary>Supplies packed parameters for one publication per use.</summary>
    CpuUniformBuffer ILumOnWorldProbeClipmapResolveShaderProgramBindings.Parameters => Params;
    #endregion
}
