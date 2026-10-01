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
[ShaderBindingSet(typeof(ShaderInterfaceLocations), Defaults = true)]
[ShaderBindingSet(typeof(ShaderIncludeBindings), Defaults = true)]
public sealed partial class LumOnHzbDownsampleShaderProgram : GpuProgram
{

    #region Private: GPU binding declarations
    /// <summary>Declares the VgeLumOnHzbDownsampleParamsUBO UniformBlock slot.</summary>
    [ShaderBinding("VgeLumOnHzbDownsampleParamsUBO", ShaderBindingKind.UniformBlock, GpuBindingRegistry.Ubo.Object, ShaderStageKind.Vertex, ShaderStageKind.Fragment)]
    private partial GpuUniformBuffer Parameters { set; }
    /// <summary>Declares the hzbDepth Sampler slot.</summary>
    [ShaderBinding("hzbDepth", ShaderBindingKind.Sampler, 0, ShaderStageKind.Vertex, ShaderStageKind.Fragment)]
    private partial GpuTexture HzbDepthTexture { set; }
    #endregion

    /// <summary>Uses the immutable declaration owned by this shader class.</summary>
    internal override GpuShaderContract ProgramContract => Contract;

    private LumOnHzbDownsampleParamsUbo? paramsUbo;

    public LumOnHzbDownsampleShaderProgram()
    {
        ProgramLayout.RegisterContract(Contract.Stages[1].Bindings);
    }

    private LumOnHzbDownsampleParamsUbo Params => paramsUbo ??= new LumOnHzbDownsampleParamsUbo();

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
    public GpuTexture? HzbDepth { set => BindTexture2D("hzbDepth", value, 0); }

    /// <summary>
    /// Source mip level to read from.
    /// </summary>
    public int SrcMip
    {
        set
        {
            Params.SrcMip = value;
            Params.BindTo(this, LumOnHzbDownsampleParamsUbo.BlockName, $"VGE.{ShaderName}.Params");
        }
    }
}
