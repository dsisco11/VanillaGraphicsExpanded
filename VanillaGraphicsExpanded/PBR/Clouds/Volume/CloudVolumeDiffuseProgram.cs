using System;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Rendering.Contracts;
using Vintagestory.API.Common;

namespace VanillaGraphicsExpanded.PBR.Clouds.Volume;

/// <summary>Gathers persistent diffuse illumination with independently compiled self-shadowing.</summary>
[ShaderProgram("Contract","cloud_volume/diffuse",2)]
[ShaderStage("Contract",ShaderStageKind.Compute,"cloud_volume/diffuse.csh")]
[ShaderUse("Contract",ShaderStageKind.Compute,nameof(SelfShadow))]
internal sealed partial class CloudVolumeDiffuseProgram:GpuComputeProgram,ICloudVolumeDiffuseBindings
{
    internal const int DefaultSlices=8;
    private readonly PackedUniformBuffer inputs=new(208);
    #region Public API
    /// <summary>Removes the direct-visibility sampler and lookup when self-shadowing is disabled.</summary>
    [ShaderOption("VGE_VOLUME_DIFFUSE_SHADOWS",true)]
    internal static partial ShaderOption<bool> SelfShadow {get;}
    /// <summary>Loads the independently selected transport variant.</summary>
    internal static CloudVolumeDiffuseProgram Create(ICoreAPI api,bool shadows)
    {
        var layout=new GpuProgramLayout();layout.RegisterContract(Contract.Stages[0].Bindings);
        if(!GpuComputePipeline.TryCreateFromAssets(api,new ShaderSettings(Contract).With(SelfShadow,shadows),out var pipeline,
            out string log,"Cloud.Volume.Diffuse",layout:layout))throw new InvalidOperationException(log);
        return new(pipeline!);
    }
    /// <summary>Stages the frozen physical scene, geometry and coherent source/destination encodings.</summary>
    internal void Capture(CloudVolumeWorkBatch batch,CloudVolumeEncoding target)
    {
        RequireInputMutation();var d=batch.Fields.Domain;var light=batch.Lighting;
        Span<byte> data=stackalloc byte[208];data.Clear();
        UboPacking.WriteIVec4(data,0,d.Size,CloudVolumeDomain.Height,d.Size,0);
        UboPacking.WriteIVec4(data,16,d.SlotX,d.SlotZ,d.BlocksX,d.BlocksY);
        UboPacking.WriteVec4(data,32,(float)(d.FirstX*CloudVolumeDomain.HorizontalCell-d.AnchorX),(float)(d.Bottom-d.VerticalCell),
            (float)(d.FirstZ*CloudVolumeDomain.HorizontalCell-d.AnchorZ),0);
        UboPacking.WriteVec4(data,48,(float)CloudVolumeDomain.HorizontalCell,d.VerticalCell,(float)CloudVolumeDomain.HorizontalCell,0);
        UboPacking.WriteVec4(data,64,0,(float)d.Bottom,0,0);
        UboPacking.WriteVec4(data,80,50*d.Width,(float)(d.Bottom+d.Band),50*d.Width,0);
        UboPacking.WriteVec4(data,96,light.Sun.X,light.Sun.Y,light.Sun.Z,0);
        UboPacking.WriteVec4(data,112,light.Moon.X,light.Moon.Y,light.Moon.Z,0);
        UboPacking.WriteVec4(data,128,light.Solar.X,light.Solar.Y,light.Solar.Z,0);
        UboPacking.WriteVec4(data,144,light.Lunar.X,light.Lunar.Y,light.Lunar.Z,0);
        UboPacking.WriteVec4(data,160,light.Environment.X,light.Environment.Y,light.Environment.Z,0);
        UboPacking.WriteVec4(data,176,target.Cap.X,target.Cap.Y,target.Cap.Z,0);
        UboPacking.WriteVec4(data,192,batch.Fields.Encoding.Scale,target.Scale,0,0);inputs.SetBytes(data);
    }
    #endregion
    #region Private
    /// <summary>Owns the executable's single packed transport record.</summary>
    private CloudVolumeDiffuseProgram(GpuComputePipeline pipeline):base(pipeline)=>OwnUniformBuffer(inputs);
    /// <summary>Supplies the complete staged gather contract.</summary>
    CpuUniformBuffer ICloudVolumeDiffuseBindings.Inputs=>inputs;
    #endregion
}
