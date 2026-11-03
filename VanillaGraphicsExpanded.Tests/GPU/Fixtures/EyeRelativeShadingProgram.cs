using System.Numerics;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Rendering.Contracts;
using VanillaGraphicsExpanded.Rendering.RHI;
using VanillaGraphicsExpanded.Rendering.Shaders;

namespace VanillaGraphicsExpanded.Tests.GPU.Fixtures;

/// <summary>Owns typed receiver, camera and material resources for eye-relative terrain helper checks.</summary>
internal sealed class EyeRelativeShadingProgram : GpuProgram
{
    private static readonly GpuShaderContract contract =
        ShaderBuildTool.Spirv.TestShaderPrograms.Create().Programs["tests/eye-relative"];
    private readonly PackedUniformBuffer inputs;
    private readonly byte[] bytes = new byte[32];
    private VgeFrameUniformBuffer? frame;
    private GpuTexture? normalDepth, displacementIndices, displacementRecords;

    #region Public API
    /// <summary>Registers the packaged shader interface and retains its numeric input block.</summary>
    public EyeRelativeShadingProgram()
    {
        inputs = OwnUniformBuffer(new PackedUniformBuffer(bytes.Length));
        foreach (var stage in contract.Stages) ProgramLayout.RegisterContract(stage.Bindings);
    }

    /// <summary>Identifies the packaged eye-relative terrain fixture.</summary>
    internal override GpuShaderContract ProgramContract => contract;

    /// <summary>Supplies the controlled view for eye-space reconstruction.</summary>
    internal VgeFrameUniformBuffer? FrameInputs { set { RequireInputMutation(); frame = value; } }

    /// <summary>Supplies the packed normal and height texture.</summary>
    internal GpuTexture? NormalDepth { set { RequireInputMutation(); normalDepth = value; } }

    /// <summary>Supplies the authored displacement index texture.</summary>
    internal GpuTexture? DisplacementIndices { set { RequireInputMutation(); displacementIndices = value; } }

    /// <summary>Supplies the authored material displacement records.</summary>
    internal GpuTexture? DisplacementRecords { set { RequireInputMutation(); displacementRecords = value; } }

    /// <summary>Stages one receiver position and diagnostic output mode.</summary>
    internal void Capture(Vector3 surface, int outputMode)
    {
        RequireInputMutation();
        UboPacking.WriteVec3(bytes, 0, surface.X, surface.Y, surface.Z);
        UboPacking.WriteInt32(bytes, 12, outputMode);
        UboPacking.WriteInt32(bytes, 16, 0);
        inputs.SetBytes(bytes);
    }
    #endregion

    #region Protected API
    /// <summary>Publishes the frame, material resources and typed receiver block together.</summary>
    protected override RHIResourceBindings CaptureBindingsCore()
    {
        return new(Shader,
            samplers:
            [
                new(GpuBindingEntry.Identity(ShaderBindingKind.Sampler, "vge_normalDepthTex"), normalDepth),
                new(GpuBindingEntry.Identity(ShaderBindingKind.Sampler, "vge_displacementTex"), displacementIndices),
                new(GpuBindingEntry.Identity(ShaderBindingKind.Sampler, "vge_displacementRecords"), displacementRecords)
            ],
            uniforms:
            [
                new(GpuBindingEntry.Identity(ShaderBindingKind.UniformBlock, "VgeFrameUBO"), frame?.Capture()),
                new(GpuBindingEntry.Identity(ShaderBindingKind.UniformBlock, "EyeInputs"), inputs.Capture())
            ]);
    }
    #endregion
}
