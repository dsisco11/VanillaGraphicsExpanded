using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Rendering.Contracts;
using VanillaGraphicsExpanded.Rendering.RHI;
using VanillaGraphicsExpanded.Rendering.Shaders;

namespace VanillaGraphicsExpanded.Tests.Support.Lighting;

/// <summary>Owns typed numerical inputs for the packaged finite-disk solar BRDF fixture.</summary>
internal sealed class SolarBrdfProgram : GpuProgram
{
    private static readonly GpuShaderContract contract =
        ShaderBuildTool.Spirv.TestShaderPrograms.Create().Programs["tests/pbr-solar-brdf"];
    private readonly PackedUniformBuffer inputs;
    private readonly byte[] bytes = new byte[64];

    #region Public API
    /// <summary>Uses the immutable test catalog declaration for the production solar BRDF include.</summary>
    internal override GpuShaderContract ProgramContract => contract;

    /// <summary>Registers the declared numeric block before the shader owner captures its requested plan.</summary>
    public SolarBrdfProgram()
    {
        inputs = OwnUniformBuffer(new PackedUniformBuffer(64));
        foreach (var stage in contract.Stages) ProgramLayout.RegisterContract(stage.Bindings);
    }

    /// <summary>Captures vectors and numeric controls in the fixture's std140 block.</summary>
    internal void Capture(System.Numerics.Vector3 normal, System.Numerics.Vector3 view,
        System.Numerics.Vector3 light, float roughness, float radius, int mode)
    {
        RequireInputMutation();
        UboPacking.WriteVec4(bytes, 0, normal.X, normal.Y, normal.Z, 0);
        UboPacking.WriteVec4(bytes, 16, view.X, view.Y, view.Z, 0);
        UboPacking.WriteVec4(bytes, 32, light.X, light.Y, light.Z, 0);
        UboPacking.WriteFloat(bytes, 48, roughness);
        UboPacking.WriteFloat(bytes, 52, radius);
        UboPacking.WriteInt32(bytes, 56, mode);
        inputs.SetBytes(bytes);
    }
    #endregion

    #region Protected API
    /// <summary>Publishes the captured numeric block through the prepared shader binding table.</summary>
    protected override RHIResourceBindings CaptureBindingsCore()
        => new(Shader, uniforms: [new(GpuBindingEntry.Identity(ShaderBindingKind.UniformBlock,
            "SolarBrdfInputs"), inputs.Capture())]);
    #endregion
}
