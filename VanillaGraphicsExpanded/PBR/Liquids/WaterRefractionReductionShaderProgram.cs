using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Rendering.Contracts;
using VanillaGraphicsExpanded.Rendering.Shaders;

namespace VanillaGraphicsExpanded.PBR.Liquids;

/// <summary>Reduces restored opaque receivers without separating radiance from their geometry.</summary>
[ShaderProgram("Contract", "water_refraction_reduce", 1)]
[ShaderStage("Contract", ShaderStageKind.Vertex, "pbr_composite.vsh", Identity = "water_refraction_reduce.vsh")]
[ShaderStage("Contract", ShaderStageKind.Fragment, "water_refraction_reduce.fsh")]
internal sealed partial class WaterRefractionReductionShaderProgram : GpuProgram, IWaterRefractionReductionBindings
{
    #region Public API
    /// <summary>Registers the reduction's explicit sampler layout.</summary>
    public WaterRefractionReductionShaderProgram()
    {
        ProgramLayout.RegisterContract(Contract.Stages[1].Bindings);
    }

    /// <summary>Loads the offline-built receiver reduction executable through the existing shader library.</summary>
    internal override GpuShaderContract ProgramContract => Contract;

    /// <summary>Borrows the restored radiance image for this reduction draw.</summary>
    public partial DynamicTexture2D? SourceColor { set; }

    /// <summary>Borrows the matching full-resolution depth image.</summary>
    public partial DynamicTexture2D? SourceDepth { set; }
    #endregion
}
