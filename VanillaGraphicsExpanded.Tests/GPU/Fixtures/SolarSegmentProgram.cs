using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Rendering.Contracts;
using VanillaGraphicsExpanded.Rendering.RHI;
using VanillaGraphicsExpanded.Rendering.Shaders;

namespace VanillaGraphicsExpanded.Tests.GPU.Fixtures;

/// <summary>Owns typed angular input for the packaged solar-segment numerical fixture.</summary>
internal sealed class SolarSegmentProgram : GpuProgram
{
    private static readonly GpuShaderContract contract =
        ShaderBuildTool.Spirv.TestShaderPrograms.Create().Programs["tests/sun-segment"];
    private readonly PackedUniformBuffer inputs;
    private readonly byte[] bytes = new byte[16];

    #region Public API
    /// <summary>Registers the packaged executable contract and retains its numeric input block.</summary>
    public SolarSegmentProgram()
    {
        inputs = OwnUniformBuffer(new PackedUniformBuffer(bytes.Length));
        foreach (var stage in contract.Stages) ProgramLayout.RegisterContract(stage.Bindings);
    }

    /// <summary>Identifies the packaged solar-segment executable.</summary>
    internal override GpuShaderContract ProgramContract => contract;

    /// <summary>Stages the signed solar elevation for the next production draw.</summary>
    internal void Capture(float elevation)
    {
        RequireInputMutation();
        UboPacking.WriteFloat(bytes, 0, elevation);
        inputs.SetBytes(bytes);
    }
    #endregion

    #region Protected API
    /// <summary>Publishes the captured segment input through its declared uniform-block binding.</summary>
    protected override RHIResourceBindings CaptureBindingsCore()
    {
        return new(Shader, uniforms:
        [new(GpuBindingEntry.Identity(ShaderBindingKind.UniformBlock, "SunSegmentInputs"), inputs.Capture())]);
    }
    #endregion
}
