using System.Numerics;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Rendering.Contracts;
using VanillaGraphicsExpanded.Rendering.Shaders;
namespace VanillaGraphicsExpanded.PBR.Postprocessing;
/// <summary>Owns the pbr_ao_filter executable and typed retained inputs.</summary>
[ShaderProgram("Contract", "pbr_ao_filter", 2)]
[ShaderStage("Contract", ShaderStageKind.Vertex, "pbr_postprocess.vsh", Identity = "pbr_ao_filter.vsh")]
[ShaderStage("Contract", ShaderStageKind.Fragment, "pbr_ao_filter.fsh")]
internal sealed partial class AmbientOcclusionFilterShaderProgram : GpuProgram, IAmbientOcclusionFilterShaderProgramBindings
{
    private readonly AmbientOcclusionInputs inputs;
    #region Public API
    /// <summary>Declares executable and reload identity.</summary>
    internal override GpuShaderContract ProgramContract => Contract;
    /// <summary>Registers bindings and owns their retained parameter storage.</summary>
    public AmbientOcclusionFilterShaderProgram() {
        inputs=OwnUniformBuffer(new AmbientOcclusionInputs());
        ProgramLayout.RegisterContract(Contract.Stages[0].Bindings);
        ProgramLayout.RegisterContract(Contract.Stages[1].Bindings);
    }
    /// <summary>Stages sourceImage without transferring ownership.</summary>
    public partial GpuTexture? SourceImage { set; }
    /// <summary>Stages depthImage without transferring ownership.</summary>
    public partial GpuTexture? DepthImage { set; }
    /// <summary>Stages surfaceImage without transferring ownership.</summary>
    public partial GpuTexture? SurfaceImage { set; }
    /// <summary>Stages the complete operation parameters before submission.</summary>
    internal void Capture(float[] inverseProjection, float[] view, Vector4 frame, Vector4 sampling, Vector4 distance)
        => inputs.Capture(inverseProjection,view,frame,sampling,distance);
    /// <summary>Publishes the retained uniform block through the generated binding layout.</summary>
    CpuUniformBuffer IAmbientOcclusionFilterShaderProgramBindings.Inputs => inputs;
    #endregion
}
