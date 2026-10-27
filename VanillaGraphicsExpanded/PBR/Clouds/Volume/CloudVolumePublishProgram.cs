using VanillaGraphicsExpanded.Rendering.ShaderCompilation;
using System;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Rendering.Contracts;
using Vintagestory.API.Common;

namespace VanillaGraphicsExpanded.PBR.Clouds.Volume;

/// <summary>Copies completed block changes into drawable resources through typed compute authority.</summary>
[ShaderProgram("Contract","cloud_volume/publish",3)]
[ShaderStage("Contract",ShaderStageKind.Compute,"cloud_volume/publish.csh")]
[ShaderUse("Contract",ShaderStageKind.Compute,nameof(Operation))]
internal sealed partial class CloudVolumePublishProgram:GpuComputeProgram,ICloudVolumePublishBindings
{
    private readonly PackedUniformBuffer inputs;
    #region Public API
    /// <summary>Selects argument initialization, metadata compaction or image copying structurally.</summary>
    [ShaderOption("VGE_VOLUME_PUBLISH_OPERATION",0,Domain=new object[]{0,1,2})]
    internal static partial ShaderOption<int> Operation {get;}
    /// <summary>Loads the publication executable without borrowing native weather resources.</summary>
    internal static CloudVolumePublishProgram Create(ICoreAPI api,int operation,PackedUniformBuffer shared)
    {
        var layout=new GpuProgramLayout();layout.RegisterContract(Contract.Stages[0].Bindings);
        if(!ComputeShaderAsset.TryCreateFromAssets(api,new ShaderSettings(Contract).With(Operation,operation),out var pipeline,out string log,
            "Cloud.Volume.Publish",layout:layout))throw new InvalidOperationException(log);
        return new(pipeline!,shared);
    }
    /// <summary>Captures complete block extents and exceptional full-copy channels.</summary>
    internal void Capture(CloudVolumeDomain domain,int full)
    {
        RequireInputMutation();Span<byte> data=stackalloc byte[16];
        UboPacking.WriteIVec4(data,0,domain.Size,domain.BlocksX,domain.BlocksY,full);inputs.SetBytes(data);
    }
    #endregion
    #region Private
    /// <summary>Borrows the owner's shared publication record for one compiled operation.</summary>
    private CloudVolumePublishProgram(ComputeShaderAsset pipeline,PackedUniformBuffer shared):base(pipeline)=>inputs=shared;
    /// <summary>Publishes the frozen copy mask.</summary>
    CpuUniformBuffer ICloudVolumePublishBindings.Inputs=>inputs;
    #endregion
}
