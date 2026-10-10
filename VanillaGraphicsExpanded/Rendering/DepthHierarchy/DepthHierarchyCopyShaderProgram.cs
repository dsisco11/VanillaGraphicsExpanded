using VanillaGraphicsExpanded.Rendering.Contracts;
using VanillaGraphicsExpanded.Rendering.Shaders;
using Vintagestory.API.Client;

namespace VanillaGraphicsExpanded.Rendering;

/// <summary>Copies corrected receiver hardware depth without resampling into the hierarchy's R32F mip zero.</summary>
[ShaderProgram("Contract", "vge_depth_copy", 1)]
[ShaderStage("Contract", ShaderStageKind.Vertex, "vge_depth_copy.vsh")]
[ShaderStage("Contract", ShaderStageKind.Fragment, "vge_depth_copy.fsh")]
public sealed partial class DepthHierarchyCopyShaderProgram : GpuProgram, IDepthHierarchyCopyShaderProgramBindings
{
    #region Public API
    /// <summary>Registers the shared copy contract.</summary>
    public DepthHierarchyCopyShaderProgram()
    {
        ProgramLayout.RegisterContract(Contract.Stages[1].Bindings);
    }

    /// <summary>Declares the shared copy program.</summary>
    public static void Register(ICoreClientAPI api)
    {
        var instance = new DepthHierarchyCopyShaderProgram
        {
            PassName = Contract.Identity,
            AssetDomain = "vanillagraphicsexpanded"
        };
        GpuShaderPrograms.Declare(api, instance);
    }

    /// <summary>Supplies the current corrected receiver depth texture.</summary>
    public partial GpuTexture? PrimaryDepth { set; }
    #endregion

    #region Binding sources
    /// <summary>Uses the immutable declaration owned by this shader class.</summary>
    internal override GpuShaderContract ProgramContract => Contract;
    #endregion
}
