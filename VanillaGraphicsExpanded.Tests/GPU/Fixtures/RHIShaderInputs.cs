using System;
using System.IO;
using System.Linq;
using VanillaGraphicsExpanded.Rendering.Contracts;
using VanillaGraphicsExpanded.Rendering.RHI;

namespace VanillaGraphicsExpanded.Tests.GPU.Fixtures;

/// <summary>Supplies canonical precompiled shader inputs to focused RHI lifecycle and execution fixtures.</summary>
internal static class RHIShaderInputs
{
    #region Internal API
    /// <summary>Reads the existing canonical test SPIR-V asset; no native compilation inputs are fabricated.</summary>
    internal static RHIShaderDescription ComputeDescription()
    {
        var plan = new ShaderLoadPlan(new ShaderSettings(GpuShaderContracts.Registry.FindProgram("tests/GpuUniformRingBufferIntegrationTests_1")));
        var selection = plan.Stages.Single();
        Assert.Equal(ShaderStageKind.Compute, selection.Stage.Kind);
        byte[] binary = File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "assets", "shaders", selection.BinaryPath));
        var values = selection.Specializations.Select(value => value.Value.Type switch
        {
            ShaderScalarType.Bool => new RHIShaderSpecialization(value.Id, value.Value.Bits != 0),
            ShaderScalarType.Int => new RHIShaderSpecialization(value.Id, unchecked((int)value.Value.Bits)),
            ShaderScalarType.UInt => new RHIShaderSpecialization(value.Id, value.Value.Bits),
            _ => new RHIShaderSpecialization(value.Id, BitConverter.UInt32BitsToSingle(value.Value.Bits))
        });
        var stage = new RHIShaderStageDescription(ShaderStage.Compute, binary, selection.Stage.EntryPoint, values);
        // Mutation of source payload and declarations after capture cannot change the pending native request.
        var description = new RHIShaderDescription(ShaderKind.Compute, [stage], selection.Stage.Bindings);
        Array.Clear(binary);
        return description;
    }
    #endregion
}
