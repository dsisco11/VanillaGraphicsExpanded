using System;
using System.Numerics;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Rendering.Contracts;
using Vintagestory.API.Common;

namespace VanillaGraphicsExpanded.PBR.Clouds.Volume;

/// <summary>Exercises stored-volume filtering and encoding through a typed diagnostic compute executable.</summary>
[ShaderProgram("Contract", "tests/cloud-volume-storage", 1)]
[ShaderStage("Contract", ShaderStageKind.Compute, "tests/cloud-volume-storage.csh")]
internal sealed partial class CloudVolumeProbeProgram : GpuComputeProgram, ICloudVolumeProbeBindings
{
    private readonly PackedUniformBuffer inputs = new(16);
    #region Public API
    /// <summary>Loads the small test fixture shader independently of the live cloud consumers.</summary>
    internal static CloudVolumeProbeProgram Create(ICoreAPI api)
    {
        var layout = new GpuProgramLayout(); layout.RegisterContract(Contract.Stages[0].Bindings);
        if (!GpuComputePipeline.TryCreateFromAssets(api, new ShaderSettings(Contract), out var pipeline,
            out string log, "Cloud.Volume.Probe", layout: layout)) throw new InvalidOperationException(log);
        return new(pipeline!);
    }
    /// <summary>Captures one bounded lookup transformed by the actual material domain.</summary>
    internal void Capture(CloudVolumeFields fields, Vector3 native)
        =>Capture(fields.View,native);
    /// <summary>Captures the same lookup against an isolated drawable publication.</summary>
    internal void Capture(CloudVolumeViewFields fields, Vector3 native)
    {
        RequireInputMutation(); var uv = fields.Domain.TexturePosition(native); Span<byte> data = stackalloc byte[16];
        UboPacking.WriteVec4(data, 0, uv.X, uv.Y, uv.Z, fields.Encoding.Scale); inputs.SetBytes(data);
    }
    #endregion
    #region Private
    /// <summary>Adopts the executable and the diagnostic query record.</summary>
    private CloudVolumeProbeProgram(GpuComputePipeline pipeline) : base(pipeline) => OwnUniformBuffer(inputs);
    /// <summary>Publishes the staged diagnostic record.</summary>
    CpuUniformBuffer ICloudVolumeProbeBindings.Inputs => inputs;
    #endregion
}
