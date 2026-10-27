using VanillaGraphicsExpanded.Rendering.ShaderCompilation;
using System;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Rendering.Contracts;
using Vintagestory.API.Common;

namespace VanillaGraphicsExpanded.PBR.Clouds.Volume;

/// <summary>Repairs a disjoint diffuse spare from bounded block history or a complete re-encode.</summary>
[ShaderProgram("Contract", "cloud_volume/copy", 1)]
[ShaderStage("Contract", ShaderStageKind.Compute, "cloud_volume/copy.csh")]
internal sealed partial class CloudVolumeCopyProgram : GpuComputeProgram, ICloudVolumeCopyBindings
{
    private readonly PackedUniformBuffer inputs = new(48);
    #region Public API
    /// <summary>Loads the independent diffuse publication executable.</summary>
    internal static CloudVolumeCopyProgram Create(ICoreAPI api)
    {
        var layout = new GpuProgramLayout(); layout.RegisterContract(Contract.Stages[0].Bindings);
        if (!ComputeShaderAsset.TryCreateFromAssets(api, new ShaderSettings(Contract), out var pipeline,
            out string log, "Cloud.Volume.Copy", layout: layout)) throw new InvalidOperationException(log);
        return new(pipeline!);
    }
    /// <summary>Captures a complete re-encode or an exact same-scale repair from retained block history.</summary>
    internal void Capture(CloudVolumeDomain domain, CloudVolumeEncoding source, CloudVolumeEncoding target, bool repair = false)
    {
        RequireInputMutation(); Span<byte> data = stackalloc byte[48]; data.Clear();
        UboPacking.WriteIVec4(data, 0, domain.Size, CloudVolumeDomain.Height, domain.Size, repair ? 1 : 0);
        UboPacking.WriteVec4(data, 16, source.Scale, target.Scale, 0, 0);
        UboPacking.WriteVec4(data, 32, target.Cap.X, target.Cap.Y, target.Cap.Z, 0); inputs.SetBytes(data);
    }
    #endregion
    #region Private
    /// <summary>Adopts the executable and its complete copy parameters.</summary>
    private CloudVolumeCopyProgram(ComputeShaderAsset pipeline) : base(pipeline) => OwnUniformBuffer(inputs);
    /// <summary>Publishes the staged field encoding record.</summary>
    CpuUniformBuffer ICloudVolumeCopyBindings.Inputs => inputs;
    #endregion
}
