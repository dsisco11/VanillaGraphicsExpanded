using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Rendering.Contracts;
using VanillaGraphicsExpanded.Rendering.RHI;
using VanillaGraphicsExpanded.Rendering.Shaders;
using VanillaGraphicsExpanded.Tests.Support.Rendering.Uniforms;
using System.Numerics;

namespace VanillaGraphicsExpanded.Tests.Support.Lighting;

/// <summary>Owns typed color and glow inputs for the shipped OIT writer's exceptional-input observation.</summary>
internal sealed class OitOwnedWriterProbeProgram : GpuProgram
{
    private static readonly GpuShaderContract contract =
        ShaderBuildTool.Spirv.TestShaderPrograms.Create().Programs["tests/oit-owned-writer"];
    private readonly PackedUniformBuffer inputs;
    private readonly byte[] bytes = new byte[32];

    #region Public API
    /// <summary>Registers the fixed color-and-glow block for the shipped owned writer.</summary>
    public OitOwnedWriterProbeProgram()
    {
        inputs = OwnUniformBuffer(new PackedUniformBuffer(bytes.Length));
        foreach (var stage in contract.Stages) ProgramLayout.RegisterContract(stage.Bindings);
    }

    /// <summary>Identifies the packaged OIT owned-writer probe executable.</summary>
    internal override GpuShaderContract ProgramContract => contract;

    /// <summary>Captures the authored RGBA and glow inputs used by the writer.</summary>
    internal void Capture(Vector4 color, float glow)
    {
        RequireInputMutation();
        UboPacking.WriteVec4(bytes, 0, color.X, color.Y, color.Z, color.W);
        UboPacking.WriteFloat(bytes, 16, glow);
        inputs.SetPayload(bytes);
    }
    #endregion

    #region Protected API
    /// <summary>Publishes the typed numeric block for the prepared OIT draw.</summary>
    protected override RHIResourceBindings CaptureBindingsCore()
        => new(Shader, uniforms: [new(GpuBindingEntry.Identity(ShaderBindingKind.UniformBlock,
            "OitOwnedWriterInputs"), inputs.Capture())]);
    #endregion
}
