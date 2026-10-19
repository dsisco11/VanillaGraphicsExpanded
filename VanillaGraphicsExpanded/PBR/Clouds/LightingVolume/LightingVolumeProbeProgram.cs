using System;
using System.Numerics;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Rendering.Contracts;
using Vintagestory.API.Common;

namespace VanillaGraphicsExpanded.PBR.Clouds.LightingVolume;

/// <summary>Exercises private candidate density and sampling helpers through production executable ownership.</summary>
[ShaderProgram("Contract","tests/cloud-lighting-volume",2)]
[ShaderStage("Contract",ShaderStageKind.Compute,"tests/cloud-lighting-volume.csh")]
[ShaderUse("Contract",ShaderStageKind.Compute,nameof(Detail))]
internal sealed partial class LightingVolumeProbeProgram : GpuComputeProgram, ILightingVolumeProbeBindings
{
    private readonly LightingVolumeParameters inputs = new();
    private readonly PackedUniformBuffer query = new(16);
    #region Public API
    /// <summary>Removes procedural density detail structurally when disabled.</summary>
    [ShaderOptionReference(typeof(LightingVolumeShaderOptions),nameof(LightingVolumeShaderOptions.Detail))]
    internal static partial ShaderOption<bool> Detail { get; }
    /// <summary>Loads only the registered test root and the selected detail variant.</summary>
    internal static LightingVolumeProbeProgram Create(ICoreAPI api,bool detail)
    {
        var layout=new GpuProgramLayout();layout.RegisterContract(Contract.Stages[0].Bindings);
        if(!GpuComputePipeline.TryCreateFromAssets(api,new ShaderSettings(Contract).With(Detail,detail),out var pipeline,
            out string log,"Cloud.LightingVolume.Probe",layout:layout)) throw new InvalidOperationException(log);
        return new(pipeline!);
    }
    /// <summary>Stages a candidate publication and one native-local query point.</summary>
    internal void Capture(LightingVolumeDomain domain,CloudPlacement placement,LightingVolumeEncoding encoding,Vector3 point)
    {
        RequireInputMutation(); inputs.Capture(domain,placement,encoding);
        Span<byte> data=stackalloc byte[16]; UboPacking.WriteVec4(data,0,point.X,point.Y,point.Z,0); query.SetBytes(data);
    }
    #endregion
    #region Private
    /// <summary>Adopts the executable and query blocks while leaving all textures with their owners.</summary>
    private LightingVolumeProbeProgram(GpuComputePipeline pipeline):base(pipeline)
    { OwnUniformBuffer(inputs); OwnUniformBuffer(query); }
    /// <summary>Publishes bounded candidate density and storage parameters.</summary>
    CpuUniformBuffer ILightingVolumeProbeBindings.Inputs=>inputs;
    /// <summary>Publishes the authored diagnostic query.</summary>
    CpuUniformBuffer ILightingVolumeProbeBindings.Query=>query;
    #endregion
}
