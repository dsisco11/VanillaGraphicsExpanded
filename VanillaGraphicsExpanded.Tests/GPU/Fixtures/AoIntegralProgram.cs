using System.Numerics;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Rendering.Contracts;
using VanillaGraphicsExpanded.Rendering.Shaders;
namespace VanillaGraphicsExpanded.Tests.GPU.Fixtures;
/// <summary>Owns typed angular inputs for the production AO integral fixture.</summary>
internal sealed class AoIntegralProgram : GpuProgram
{
    private static readonly GpuShaderContract contract=ShaderBuildTool.Spirv.TestShaderPrograms.Create().Programs["tests/ao-integral"];
    private readonly PackedUniformBuffer inputs;
    private readonly byte[] bytes=new byte[16];
    #region Public API
    /// <summary>Registers the test executable and owns its angular parameter block.</summary>
    public AoIntegralProgram() {inputs=OwnUniformBuffer(new PackedUniformBuffer(16));foreach(var stage in contract.Stages)ProgramLayout.RegisterContract(stage.Bindings);}
    /// <summary>Selects the packaged numerical fixture.</summary>
    internal override GpuShaderContract ProgramContract=>contract;
    /// <summary>Stages normal angle and signed visible horizons through the typed block.</summary>
    internal void Capture(float normal,float low,float high) {RequireInputMutation();UboPacking.WriteVec3(bytes,0,normal,low,high);UboPacking.WriteFloat(bytes,12,0);inputs.SetBytes(bytes);}
    /// <summary>Stages one ordered horizon update with a dimensionless release fraction.</summary>
    internal void CaptureRelaxation(float horizon,float candidate,float relaxation) {
        RequireInputMutation();UboPacking.WriteVec3(bytes,0,horizon,candidate,relaxation);
        UboPacking.WriteFloat(bytes,12,1);inputs.SetBytes(bytes);
    }
    #endregion
    #region Protected API
    /// <summary>Publishes the complete immutable angular parameter slice.</summary>
    protected override void Submit() {
        var block=ShaderPreparedSubmission.Resolve(this,GpuBindingEntry.Identity(ShaderBindingKind.UniformBlock,"AoIntegralTestInputs"));
        ShaderPreparedSubmission.ValidateUniformBlock(block,inputs);ShaderPreparedSubmission.UniformBlock(block,inputs);
    }
    #endregion
}
