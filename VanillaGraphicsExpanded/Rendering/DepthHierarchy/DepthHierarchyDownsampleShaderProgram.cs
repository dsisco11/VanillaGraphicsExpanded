using VanillaGraphicsExpanded.Rendering.Contracts;
using VanillaGraphicsExpanded.Rendering.Shaders;
using Vintagestory.API.Client;

namespace VanillaGraphicsExpanded.Rendering;

/// <summary>Reduces one hardware-depth mip into the next using conservative proportional footprints.</summary>
[ShaderProgram("Contract", "vge_depth_reduce", 1)]
[ShaderStage("Contract", ShaderStageKind.Vertex, "vge_depth_reduce.vsh")]
[ShaderStage("Contract", ShaderStageKind.Fragment, "vge_depth_reduce.fsh")]
public sealed partial class DepthHierarchyDownsampleShaderProgram : GpuProgram, IDepthHierarchyDownsampleShaderProgramBindings
{
    private DepthHierarchyDownsampleParamsUbo? paramsUbo;

    #region Public API
    /// <summary>Registers the shared reduction contract.</summary>
    public DepthHierarchyDownsampleShaderProgram()
    {
        ProgramLayout.RegisterContract(Contract.Stages[1].Bindings);
    }

    /// <summary>Declares the shared reduction program.</summary>
    public static void Register(ICoreClientAPI api)
    {
        var instance = new DepthHierarchyDownsampleShaderProgram
        {
            PassName = Contract.Identity,
            AssetDomain = "vanillagraphicsexpanded"
        };
        GpuShaderPrograms.Declare(api, instance);
    }

    /// <summary>Supplies the mipmapped R32F minimum-depth texture.</summary>
    public partial GpuTexture? HzbDepth { set; }

    /// <summary>Selects a source mip relative to the texture's accessible base level.</summary>
    public int SrcMip { set => Params.SrcMip = value; }
    #endregion

    #region Binding sources
    /// <summary>Uses the immutable declaration owned by this shader class.</summary>
    internal override GpuShaderContract ProgramContract => Contract;

    /// <summary>Supplies the retained CPU block for one publication per use.</summary>
    CpuUniformBuffer IDepthHierarchyDownsampleShaderProgramBindings.Parameters => Params;
    #endregion

    #region Private
    /// <summary>Owns parameter publication versions through terminal shader retirement.</summary>
    private DepthHierarchyDownsampleParamsUbo Params =>
        paramsUbo ??= OwnUniformBuffer(new DepthHierarchyDownsampleParamsUbo());
    #endregion
}
