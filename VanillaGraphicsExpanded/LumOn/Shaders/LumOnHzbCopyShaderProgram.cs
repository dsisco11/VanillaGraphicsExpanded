using VanillaGraphicsExpanded.Rendering.Contracts;
using Vintagestory.API.Client;
using Vintagestory.Client.NoObf;

using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Rendering.Shaders;

namespace VanillaGraphicsExpanded.LumOn;

/// <summary>
/// Shader program for building HZB mip 0 from the primary depth texture.
/// Outputs raw depth (0..1) into an R32F render target.
/// </summary>
[ShaderProgram("Contract", "lumon_hzb_copy", 1)]
[ShaderStage("Contract", ShaderStageKind.Vertex, "lumon_hzb_copy.vsh")]
[ShaderStage("Contract", ShaderStageKind.Fragment, "lumon_hzb_copy.fsh")]
[ShaderBindingSet(typeof(ShaderInterfaceLocations), Defaults = true)]
[ShaderBindingSet(typeof(ShaderIncludeBindings), Defaults = true)]
public sealed partial class LumOnHzbCopyShaderProgram : GpuProgram
{

    #region Private: GPU binding declarations
    /// <summary>Declares the primaryDepth Sampler slot.</summary>
    [ShaderBinding("primaryDepth", ShaderBindingKind.Sampler, 0, ShaderStageKind.Vertex, ShaderStageKind.Fragment)]
    private partial GpuTexture PrimaryDepthTexture { set; }
    #endregion

    /// <summary>Uses the immutable declaration owned by this shader class.</summary>
    internal override GpuShaderContract ProgramContract => Contract;

    public LumOnHzbCopyShaderProgram()
    {
        ProgramLayout.RegisterContract(Contract.Stages[1].Bindings);

    }

    public static void Register(ICoreClientAPI api)
    {
        var instance = new LumOnHzbCopyShaderProgram
        {
            PassName = Contract.Identity,
            AssetDomain = "vanillagraphicsexpanded"
        };
        global::VanillaGraphicsExpanded.Rendering.Shaders.GpuShaderPrograms.Declare(api, instance);
    }

    /// <summary>
    /// Primary depth texture (VS depth buffer).
    /// </summary>
    public int PrimaryDepth { set => BindExternalTexture2D("primaryDepth", value, 0, GpuSamplers.NearestClamp); }
}
