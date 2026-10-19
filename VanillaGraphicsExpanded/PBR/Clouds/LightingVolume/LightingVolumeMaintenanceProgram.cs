using System;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Rendering.Contracts;
using Vintagestory.API.Common;

namespace VanillaGraphicsExpanded.PBR.Clouds.LightingVolume;

/// <summary>Initializes, remaps and re-encodes private lighting storage without borrowing cloud transport.</summary>
[ShaderProgram("Contract", "cloud_lighting_volume/maintenance", 1)]
[ShaderStage("Contract", ShaderStageKind.Compute, "cloud_lighting_volume/maintenance.csh")]
internal sealed partial class LightingVolumeMaintenanceProgram : GpuComputeProgram, ILightingVolumeMaintenanceBindings
{
    private readonly PackedUniformBuffer inputs = new(96);
    #region Public API
    /// <summary>Loads the independently discovered candidate maintenance root.</summary>
    internal static LightingVolumeMaintenanceProgram Create(ICoreAPI api)
    {
        var layout = new GpuProgramLayout(); layout.RegisterContract(Contract.Stages[0].Bindings);
        if (!GpuComputePipeline.TryCreateFromAssets(api, new ShaderSettings(Contract), out var pipeline,
            out string log, "Cloud.LightingVolume.Maintenance", layout: layout)) throw new InvalidOperationException(log);
        return new(pipeline!);
    }

    /// <summary>Stages bounded relative lattice offsets; mode zero initializes, one remaps, two copies/rescales.</summary>
    internal void Capture(LightingVolumeDomain target, LightingVolumeDomain? source,
        LightingVolumeEncoding oldEncoding, LightingVolumeEncoding newEncoding, uint generation, int mode)
    {
        RequireInputMutation();
        Span<byte> data = stackalloc byte[96]; data.Clear();
        UboPacking.WriteIVec4(data, 0, target.Size, LightingVolumeDomain.Height, target.Size, mode);
        UboPacking.WriteIVec4(data, 16, source is null ? 0 : checked((int)(target.X-source.X)), 0,
            source is null ? 0 : checked((int)(target.Z-source.Z)), target.Interior);
        UboPacking.WriteIVec4(data, 32, target.RegionSide, 8, target.RegionSide, unchecked((int)generation));
        UboPacking.WriteIVec4(data, 48, source is null ? 0 : checked((int)(target.RegionX-source.RegionX)), 0,
            source is null ? 0 : checked((int)(target.RegionZ-source.RegionZ)), 0);
        UboPacking.WriteVec4(data, 64, oldEncoding.Scale, newEncoding.Scale, newEncoding.Cap, 0);
        UboPacking.WriteIVec4(data, 80, (int)(target.X-4*target.RegionX), 0, (int)(target.Z-4*target.RegionZ), 0);
        inputs.SetBytes(data);
    }
    #endregion
    #region Private
    /// <summary>Adopts the executable and its owned parameter block.</summary>
    private LightingVolumeMaintenanceProgram(GpuComputePipeline pipeline) : base(pipeline) => OwnUniformBuffer(inputs);
    /// <summary>Publishes the complete staged maintenance record.</summary>
    CpuUniformBuffer ILightingVolumeMaintenanceBindings.Inputs => inputs;
    #endregion
}
