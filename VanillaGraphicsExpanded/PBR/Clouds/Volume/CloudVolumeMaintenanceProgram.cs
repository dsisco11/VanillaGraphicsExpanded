using VanillaGraphicsExpanded.Rendering.ShaderCompilation;
using System;
using System.Numerics;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Rendering.Contracts;
using Vintagestory.API.Common;

namespace VanillaGraphicsExpanded.PBR.Clouds.Volume;

/// <summary>Initializes bounded block batches and resets entering circular slots through a typed compute program.</summary>
[ShaderProgram("Contract", "cloud_volume/maintenance", 1)]
[ShaderStage("Contract", ShaderStageKind.Compute, "cloud_volume/maintenance.csh")]
internal sealed partial class CloudVolumeMaintenanceProgram : GpuComputeProgram, ICloudVolumeMaintenanceBindings
{
    private readonly CloudVolumeMaintenanceParameters inputs = new();
    #region Public API
    /// <summary>Loads only the independently owned maintenance executable.</summary>
    internal static CloudVolumeMaintenanceProgram Create(ICoreAPI api)
    {
        var layout = new GpuProgramLayout(); layout.RegisterContract(Contract.Stages[0].Bindings);
        if (!ComputeShaderAsset.TryCreateFromAssets(api, new ShaderSettings(Contract), out var pipeline,
            out string log, "Cloud.Volume.Maintenance", layout: layout)) throw new InvalidOperationException(log);
        return new(pipeline!);
    }
    /// <summary>Captures one finite block range and an optional old material window for retention.</summary>
    internal void Capture(CloudVolumeDomain target, CloudVolumeDomain? source, Vector3 fallback, int start, int count)
    {
        RequireInputMutation();
        inputs.Capture(target, source, fallback, start, count);
    }
    #endregion
    #region Private
    /// <summary>Adopts the pipeline and its one parameter buffer.</summary>
    private CloudVolumeMaintenanceProgram(ComputeShaderAsset pipeline) : base(pipeline) => OwnUniformBuffer(inputs);
    /// <summary>Publishes the captured maintenance record through the generated binding contract.</summary>
    CpuUniformBuffer ICloudVolumeMaintenanceBindings.Inputs => inputs;
    #endregion
}
