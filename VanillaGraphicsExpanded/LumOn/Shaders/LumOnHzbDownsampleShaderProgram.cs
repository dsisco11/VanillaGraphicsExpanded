using VanillaGraphicsExpanded.Rendering.Contracts;
using Vintagestory.API.Client;
using Vintagestory.Client.NoObf;

using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Rendering.Shaders;
using VanillaGraphicsExpanded.LumOn.Shaders;

namespace VanillaGraphicsExpanded.LumOn;

/// <summary>
/// Shader program for downsampling an HZB mip level into the next mip.
/// Uses MIN depth over a 2x2 block.
/// </summary>
[ShaderProgram("Contract", "lumon_hzb_downsample", 1)]
[ShaderStage("Contract", ShaderStageKind.Vertex, "lumon_hzb_downsample.vsh")]
[ShaderStage("Contract", ShaderStageKind.Fragment, "lumon_hzb_downsample.fsh")]
public sealed partial class LumOnHzbDownsampleShaderProgram : GpuProgram, ILumOnHzbDownsampleShaderProgramBindings
{


    /// <summary>Uses the immutable declaration owned by this shader class.</summary>
    internal override GpuShaderContract ProgramContract => Contract;

    private LumOnHzbDownsampleParamsUbo? paramsUbo;

    public LumOnHzbDownsampleShaderProgram()
    {
        ProgramLayout.RegisterContract(Contract.Stages[1].Bindings);
    }

    /// <summary>Owns packed parameters and rejects writes during publication.</summary>
    private LumOnHzbDownsampleParamsUbo Params
    {
        get
        {
            if (paramsUbo is null)
            {
                paramsUbo = new LumOnHzbDownsampleParamsUbo();
                paramsUbo.SetWriteGuard(RequireInputMutation);
            }
            return paramsUbo;
        }
    }

    public static void Register(ICoreClientAPI api)
    {
        var instance = new LumOnHzbDownsampleShaderProgram
        {
            PassName = Contract.Identity,
            AssetDomain = "vanillagraphicsexpanded"
        };
        global::VanillaGraphicsExpanded.Rendering.Shaders.GpuShaderPrograms.Declare(api, instance);
    }

    /// <summary>
    /// HZB depth texture (mipmapped R32F).
    /// </summary>
    public partial GpuTexture? HzbDepth { set; }

    /// <summary>
    /// Source mip level to read from.
    /// </summary>
    public int SrcMip
    {
        set
        {
            Params.SrcMip = value;
        }
    }
    #region Binding sources
    /// <summary>Supplies the retained CPU block for one publication per use.</summary>
    CpuUniformBuffer ILumOnHzbDownsampleShaderProgramBindings.Parameters => Params;
    #endregion
}
