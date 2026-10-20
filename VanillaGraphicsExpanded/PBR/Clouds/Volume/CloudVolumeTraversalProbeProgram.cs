using System;
using System.Numerics;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Rendering.Contracts;
using Vintagestory.API.Common;

namespace VanillaGraphicsExpanded.PBR.Clouds.Volume;

/// <summary>Runs the production traversal helper with authored fields without importing either old cloud renderer.</summary>
[ShaderProgram("Contract","tests/cloud-volume-traversal",1)]
[ShaderStage("Contract",ShaderStageKind.Compute,"tests/cloud-volume-traversal.csh")]
internal sealed partial class CloudVolumeTraversalProbeProgram:GpuComputeProgram,ICloudVolumeTraversalProbeBindings
{
    private readonly PackedUniformBuffer inputs=new(128);
    #region Public API
    /// <summary>Loads the one diagnostic traversal shader.</summary>
    internal static CloudVolumeTraversalProbeProgram Create(ICoreAPI api)
    {
        var layout=new GpuProgramLayout();layout.RegisterContract(Contract.Stages[0].Bindings);
        if(!GpuComputePipeline.TryCreateFromAssets(api,new ShaderSettings(Contract),out var pipeline,out string log,
            "Cloud.Volume.TraversalProbe",layout:layout))throw new InvalidOperationException(log);
        return new(pipeline!);
    }
    /// <summary>Captures native-local geometry and optional deliberately reduced test operation cap.</summary>
    internal void Capture(CloudVolumeDomain d,Vector3 origin,Vector3 direction,float length,float fraction,int limit=0)
    {
        RequireInputMutation();Span<byte> data=stackalloc byte[128];data.Clear();
        UboPacking.WriteIVec4(data,0,d.Size,CloudVolumeDomain.Height,d.Size,limit);
        UboPacking.WriteIVec4(data,16,d.SlotX,d.SlotZ,d.BlocksX,d.BlocksY);
        UboPacking.WriteVec4(data,32,(float)(d.FirstX*CloudVolumeDomain.HorizontalCell-d.AnchorX),(float)(d.Bottom-d.VerticalCell),
            (float)(d.FirstZ*CloudVolumeDomain.HorizontalCell-d.AnchorZ),0);
        UboPacking.WriteVec4(data,48,(float)CloudVolumeDomain.HorizontalCell,d.VerticalCell,(float)CloudVolumeDomain.HorizontalCell,0);
        UboPacking.WriteVec4(data,64,0,(float)d.Bottom,0,0);
        UboPacking.WriteVec4(data,80,50*d.Width,(float)(d.Bottom+d.Band),50*d.Width,0);
        UboPacking.WriteVec4(data,96,origin.X,origin.Y,origin.Z,length);
        UboPacking.WriteVec4(data,112,direction.X,direction.Y,direction.Z,fraction);inputs.SetBytes(data);
    }
    #endregion
    #region Private
    /// <summary>Adopts the executable and its authored query.</summary>
    private CloudVolumeTraversalProbeProgram(GpuComputePipeline pipeline):base(pipeline)=>OwnUniformBuffer(inputs);
    /// <summary>Publishes the staged bounded diagnostic record.</summary>
    CpuUniformBuffer ICloudVolumeTraversalProbeBindings.Inputs=>inputs;
    #endregion
}
