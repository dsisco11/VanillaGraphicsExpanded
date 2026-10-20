using System;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Rendering.Contracts;
using Vintagestory.API.Common;

namespace VanillaGraphicsExpanded.PBR.Clouds.Volume;

/// <summary>Owns one structural generation operation, compiling procedural detail out when disabled.</summary>
[ShaderProgram("Contract","cloud_volume/density",28)]
[ShaderStage("Contract",ShaderStageKind.Compute,"cloud_volume/density.csh")]
[ShaderUse("Contract",ShaderStageKind.Compute,nameof(Operation))]
[ShaderUse("Contract",ShaderStageKind.Compute,nameof(Detail))]
internal sealed partial class CloudVolumeDensityProgram : GpuComputeProgram, ICloudVolumeDensityBindings
{
    private readonly PackedUniformBuffer inputs;
    #region Public API
    /// <summary>Selects validation, observation, selection, generation, occupancy repair or completion.</summary>
    [ShaderOption("VGE_VOLUME_DENSITY_OPERATION",0,Domain=new object[]{0,1,2,3,4,5,6,7,8,9,10,11,12,13})]
    internal static partial ShaderOption<int> Operation { get; }
    /// <summary>Removes generation-only noise and erosion when disabled.</summary>
    [ShaderOption("VGE_VOLUME_DENSITY_DETAIL",true)]
    internal static partial ShaderOption<bool> Detail { get; }
    /// <summary>Loads only the selected structural operation and density variant.</summary>
    internal static CloudVolumeDensityProgram Create(ICoreAPI api,int operation,bool detail,PackedUniformBuffer? shared=null)
    {
        var layout=new GpuProgramLayout();layout.RegisterContract(Contract.Stages[0].Bindings);
        if(!GpuComputePipeline.TryCreateFromAssets(api,new ShaderSettings(Contract).With(Operation,operation).With(Detail,detail),
            out var pipeline,out string log,"Cloud.Volume.Density",layout:layout)) throw new InvalidOperationException(log);
        return new(pipeline!,shared);
    }
    /// <summary>Captures relative keys and physical units without persisting a native map handle.</summary>
    internal void Capture(CloudVolumeDomain domain,long x,long z,CloudVolumeDensityState state,uint frame,bool initial,int radixShift=0)
    {
        RequireInputMutation();Span<byte> data=stackalloc byte[128];data.Clear();
        bool history=state.SnapshotValid && Math.Abs(x-state.WindowX)<domain.Width && Math.Abs(z-state.WindowZ)<domain.Width;
        UboPacking.WriteIVec4(data,0,domain.Size,CloudVolumeDomain.Height,domain.Width,domain.BlocksX);
        UboPacking.WriteIVec4(data,16,domain.SlotX,domain.SlotZ,CloudVolumeDomain.Mod(x,domain.Width),CloudVolumeDomain.Mod(z,domain.Width));
        UboPacking.WriteIVec4(data,32,history?checked((int)(state.WindowX-x)):0,history?checked((int)(state.WindowZ-z)):0,history?1:0,0);
        UboPacking.WriteVec4(data,48,(float)(domain.FirstX*CloudVolumeDomain.HorizontalCell-domain.AnchorX),
            (float)(domain.Bottom-domain.VerticalCell),(float)(domain.FirstZ*CloudVolumeDomain.HorizontalCell-domain.AnchorZ),0);
        UboPacking.WriteVec4(data,64,(float)CloudVolumeDomain.HorizontalCell,domain.VerticalCell,(float)CloudVolumeDomain.HorizontalCell,0);
        UboPacking.WriteIVec4(data,80,unchecked((int)frame),unchecked((int)state.Revision),domain.Blocks,initial?1:0);
        UboPacking.WriteIVec4(data,96,4096,domain.BlocksY,unchecked((int)state.SnapshotFrame),radixShift);
        UboPacking.WriteVec4(data,112,(float)((domain.AnchorX%12800+12800)%12800),0,
            (float)((domain.AnchorZ%12800+12800)%12800),0);inputs.SetBytes(data);
    }
    #endregion
    #region Private
    /// <summary>Adopts the pipeline and its bounded uniform record.</summary>
    private CloudVolumeDensityProgram(GpuComputePipeline pipeline,PackedUniformBuffer? shared):base(pipeline)
    {inputs=shared??OwnUniformBuffer(new PackedUniformBuffer(128));}
    /// <summary>Publishes the captured generation identity.</summary>
    CpuUniformBuffer ICloudVolumeDensityBindings.Inputs=>inputs;
    #endregion
}
