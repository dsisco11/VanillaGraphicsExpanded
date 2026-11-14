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
    private readonly CloudVolumeCopyParameters inputs = new();
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
        RequireInputMutation();
        inputs.Capture(domain, source, target, repair);
    }
    #endregion
    #region Private
    /// <summary>Adopts the executable and its complete copy parameters.</summary>
    private CloudVolumeCopyProgram(ComputeShaderAsset pipeline) : base(pipeline) => OwnUniformBuffer(inputs);
    /// <summary>Publishes the staged field encoding record.</summary>
    CpuUniformBuffer ICloudVolumeCopyBindings.Inputs => inputs;
    #endregion
}
