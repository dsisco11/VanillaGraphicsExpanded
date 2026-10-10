using VanillaGraphicsExpanded.Rendering.Contracts;
using VanillaGraphicsExpanded.Rendering.Shaders;
using Vintagestory.API.Client;

namespace VanillaGraphicsExpanded.Rendering;

/// <summary>Copies corrected receiver hardware depth without resampling into the hierarchy's R32F mip zero.</summary>
[ShaderProgram("Contract", "tests/depth_raster_copy", 1)]
[ShaderStage("Contract", ShaderStageKind.Vertex, "tests/depth_raster_copy.vsh")]
[ShaderStage("Contract", ShaderStageKind.Fragment, "tests/depth_raster_copy.fsh")]
public sealed partial class RasterDepthCopyShaderProgram : GpuProgram, IRasterDepthCopyShaderBindings
{
    #region Public API
    /// <summary>Registers the shared copy contract.</summary>
    public RasterDepthCopyShaderProgram()
    {
        ProgramLayout.RegisterContract(Contract.Stages[1].Bindings);
    }

    /// <summary>Declares the shared copy program.</summary>
    public static void Register(ICoreClientAPI api)
    {
        var instance = new RasterDepthCopyShaderProgram
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
