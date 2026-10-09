using System.Numerics;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Rendering.Contracts;
using VanillaGraphicsExpanded.Rendering.Shaders;

namespace VanillaGraphicsExpanded.Tests.GPU.Fixtures;

/// <summary>Submits a terrain main transformed by the production material capture patch.</summary>
internal abstract class TerrainCaptureProgram : GpuProgram
{
    private readonly GpuShaderContract contract;
    private readonly PackedUniformBuffer inputs;
    private readonly byte[] bytes = new byte[32];
    #region Public API
    /// <summary>Selects the independently authored main for one installed terrain family.</summary>
    internal TerrainCaptureProgram(bool topsoil)
    {
        contract = ShaderBuildTool.Spirv.TestShaderPrograms.Create().Programs["tests/terrain-capture-" + (topsoil ? "topsoil" : "opaque")];
        inputs = OwnUniformBuffer(new PackedUniformBuffer(32));
        foreach (var stage in contract.Stages) ProgramLayout.RegisterContract(stage.Bindings);
    }
    /// <summary>Identifies the transformed fixture executable.</summary>
    internal override GpuShaderContract ProgramContract => contract;
    /// <summary>Stages authored material alpha and the original cutout threshold.</summary>
    internal void Capture(Vector4 material, float cutoff)
    {
        RequireInputMutation();
        UboPacking.WriteVec4(bytes, 0, material.X, material.Y, material.Z, material.W);
        UboPacking.WriteVec4(bytes, 16, cutoff, 0, 0, 0);
        inputs.SetBytes(bytes);
    }
    #endregion
    #region Protected API
    /// <summary>Validates and publishes the numeric block through the prepared shader contract.</summary>
    protected override void Submit()
    {
        var block = ShaderPreparedSubmission.Resolve(this, GpuBindingEntry.Identity(ShaderBindingKind.UniformBlock, "TerrainCaptureInputs"));
        ShaderPreparedSubmission.ValidateUniformBlock(block, inputs);
        ShaderPreparedSubmission.UniformBlock(block, inputs);
    }
    #endregion
}

/// <summary>Selects opaque terrain capture for typed fixture construction.</summary>
internal sealed class OpaqueTerrainCaptureProgram : TerrainCaptureProgram
{
    /// <summary>Uses the opaque terrain transform.</summary>
    public OpaqueTerrainCaptureProgram() : base(false) { }
}

/// <summary>Selects topsoil terrain capture for typed fixture construction.</summary>
internal sealed class TopsoilTerrainCaptureProgram : TerrainCaptureProgram
{
    /// <summary>Uses the topsoil terrain transform.</summary>
    public TopsoilTerrainCaptureProgram() : base(true) { }
}
