using VanillaGraphicsExpanded.Rendering.ShaderCompilation;
using System;
using System.Numerics;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Rendering.Contracts;
using Vintagestory.API.Common;

namespace VanillaGraphicsExpanded.PBR.Clouds.Volume;

/// <summary>Compiles activation initialization and each source integral independently.</summary>
[ShaderProgram("Contract","cloud_volume/direct",15)]
[ShaderStage("Contract",ShaderStageKind.Compute,"cloud_volume/direct.csh")]
[ShaderUse("Contract",ShaderStageKind.Compute,nameof(Operation))]
[ShaderUse("Contract",ShaderStageKind.Compute,nameof(Quality))]
internal sealed partial class CloudVolumeDirectProgram:GpuComputeProgram,ICloudVolumeDirectBindings
{
    internal const int DefaultWidth=8;
    private readonly PackedUniformBuffer inputs=new(128);
    #region Public API
    /// <summary>Selects one compiled workload profile for the entire executable lifetime.</summary>
    [ShaderOptionReference(typeof(CloudVolumeQualityOptions),nameof(CloudVolumeQualityOptions.Level))]
    internal static partial ShaderOption<int> Quality {get;}
    /// <summary>Selects initialization, solar or lunar transport as actual shader variants.</summary>
    [ShaderOption("VGE_VOLUME_DIRECT_OPERATION",0,Domain=new object[]{0,1,2})]
    internal static partial ShaderOption<int> Operation {get;}
    /// <summary>Loads only the independently selected structural operation.</summary>
    internal static CloudVolumeDirectProgram Create(ICoreAPI api,int operation,int quality=2)
    {
        var layout=new GpuProgramLayout();layout.RegisterContract(Contract.Stages[0].Bindings);
        if(!ComputeShaderAsset.TryCreateFromAssets(api,new ShaderSettings(Contract).With(Operation,operation).With(Quality,quality),out var pipeline,
            out string log,"Cloud.Volume.Direct",layout:layout))throw new InvalidOperationException(log);
        return new(pipeline!);
    }
    /// <summary>Freezes bounded native-local coordinates, direction and newly activated channel mask.</summary>
    internal void Capture(CloudVolumeDomain d,Vector3 direction,int newlyActive)
    {
        RequireInputMutation();Span<byte> data=stackalloc byte[128];data.Clear();
        UboPacking.WriteIVec4(data,0,d.Size,CloudVolumeDomain.Height,d.Size,0);
        UboPacking.WriteIVec4(data,16,d.SlotX,d.SlotZ,d.BlocksX,d.BlocksY);
        UboPacking.WriteVec4(data,32,(float)(d.FirstX*CloudVolumeDomain.HorizontalCell-d.AnchorX),(float)(d.Bottom-d.VerticalCell),
            (float)(d.FirstZ*CloudVolumeDomain.HorizontalCell-d.AnchorZ),0);
        UboPacking.WriteVec4(data,48,(float)CloudVolumeDomain.HorizontalCell,d.VerticalCell,(float)CloudVolumeDomain.HorizontalCell,0);
        UboPacking.WriteVec4(data,64,0,(float)d.Bottom,0,0);
        UboPacking.WriteVec4(data,80,50*d.Width,(float)(d.Bottom+d.Band),50*d.Width,0);
        UboPacking.WriteVec4(data,96,direction.X,direction.Y,direction.Z,0);
        UboPacking.WriteIVec4(data,112,newlyActive,0,0,0);inputs.SetBytes(data);
    }
    #endregion
    #region Private
    /// <summary>Adopts the executable and its single immutable parameter record.</summary>
    private CloudVolumeDirectProgram(ComputeShaderAsset pipeline):base(pipeline)=>OwnUniformBuffer(inputs);
    /// <summary>Supplies the complete staged transport record.</summary>
    CpuUniformBuffer ICloudVolumeDirectBindings.Inputs=>inputs;
    #endregion
}
