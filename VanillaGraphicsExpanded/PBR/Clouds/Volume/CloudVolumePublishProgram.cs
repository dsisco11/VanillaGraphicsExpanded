using System;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Rendering.Contracts;
using Vintagestory.API.Common;

namespace VanillaGraphicsExpanded.PBR.Clouds.Volume;

/// <summary>Copies completed block changes into drawable resources through typed compute authority.</summary>
[ShaderProgram("Contract","cloud_volume/publish",1)]
[ShaderStage("Contract",ShaderStageKind.Compute,"cloud_volume/publish.csh")]
internal sealed partial class CloudVolumePublishProgram:GpuComputeProgram,ICloudVolumePublishBindings
{
    private readonly PackedUniformBuffer inputs=new(16);
    #region Public API
    /// <summary>Loads the publication executable without borrowing native weather resources.</summary>
    internal static CloudVolumePublishProgram Create(ICoreAPI api)
    {
        var layout=new GpuProgramLayout();layout.RegisterContract(Contract.Stages[0].Bindings);
        if(!GpuComputePipeline.TryCreateFromAssets(api,new ShaderSettings(Contract),out var pipeline,out string log,
            "Cloud.Volume.Publish",layout:layout))throw new InvalidOperationException(log);
        return new(pipeline!);
    }
    /// <summary>Captures complete block extents and exceptional full-copy channels.</summary>
    internal void Capture(CloudVolumeDomain domain,int full)
    {
        RequireInputMutation();Span<byte> data=stackalloc byte[16];
        UboPacking.WriteIVec4(data,0,domain.Size,domain.BlocksX,domain.BlocksY,full);inputs.SetBytes(data);
    }
    #endregion
    #region Private
    /// <summary>Adopts the executable and its fixed publication parameters.</summary>
    private CloudVolumePublishProgram(GpuComputePipeline pipeline):base(pipeline)=>OwnUniformBuffer(inputs);
    /// <summary>Publishes the frozen copy mask.</summary>
    CpuUniformBuffer ICloudVolumePublishBindings.Inputs=>inputs;
    #endregion
}
