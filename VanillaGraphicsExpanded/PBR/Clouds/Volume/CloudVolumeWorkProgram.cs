using System;
using System.Numerics;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Rendering.Contracts;
using Vintagestory.API.Common;

namespace VanillaGraphicsExpanded.PBR.Clouds.Volume;

/// <summary>Selects or completes bounded lighting work through independently compiled structural operations.</summary>
[ShaderProgram("Contract","cloud_volume/work",22)]
[ShaderStage("Contract",ShaderStageKind.Compute,"cloud_volume/work.csh")]
[ShaderUse("Contract",ShaderStageKind.Compute,nameof(Operation))]
[ShaderUse("Contract",ShaderStageKind.Compute,nameof(LargeBudget))]
internal sealed partial class CloudVolumeWorkProgram : GpuComputeProgram,ICloudVolumeWorkBindings
{
    private readonly PackedUniformBuffer inputs;
    #region Public API
    /// <summary>Selects list production or successful-only completion metadata writes.</summary>
    [ShaderOption("VGE_VOLUME_WORK_OPERATION",0,Domain=new object[]{0,1,2,3,4,5,6,7,8,9,10})]
    internal static partial ShaderOption<int> Operation { get; }
    /// <summary>Expands shared storage when bounded occupied-origin refill needs more than 64 candidate jobs.</summary>
    [ShaderOption("VGE_VOLUME_WORK_LARGE_BUDGET",false)]
    internal static partial ShaderOption<bool> LargeBudget {get;}
    /// <summary>Loads only the selected work executable.</summary>
    internal static CloudVolumeWorkProgram Create(ICoreAPI api,int operation,bool largeBudget=false,PackedUniformBuffer? shared=null)
    {
        var layout=new GpuProgramLayout();layout.RegisterContract(Contract.Stages[0].Bindings);
        if(!GpuComputePipeline.TryCreateFromAssets(api,new ShaderSettings(Contract).With(Operation,operation).With(LargeBudget,largeBudget),out var pipeline,
            out string log,"Cloud.Volume.Work",layout:layout))throw new InvalidOperationException(log);
        return new(pipeline!,shared);
    }
    /// <summary>Stages one immutable list identity and region bounds; completion uses the same snapshot.</summary>
    internal void Capture(CloudVolumeDomain domain,uint frame,uint revision,int active,int dirty,int newlyActive,
        bool diffuse,int completed,ReadOnlySpan<Vector4> planes,int source=0,int destination=0,int directGroups=0,int diffuseGroups=0,
        bool densityPending=false,int lightingRays=CloudVolumeLightingBudget.DefaultRays,int selectionLevel=0)
    {
        RequireInputMutation();Span<byte> data=stackalloc byte[208];data.Clear();
        UboPacking.WriteIVec4(data,0,domain.BlocksX,domain.BlocksY,domain.BlocksX,domain.Blocks);
        UboPacking.WriteIVec4(data,16,unchecked((int)frame),unchecked((int)revision),active,dirty);
        UboPacking.WriteIVec4(data,32,domain.SlotX,domain.SlotZ,domain.Size,completed);
        UboPacking.WriteVec4(data,48,(float)(domain.FirstX*CloudVolumeDomain.HorizontalCell-domain.AnchorX),
            (float)(domain.Bottom-domain.VerticalCell),(float)(domain.FirstZ*CloudVolumeDomain.HorizontalCell-domain.AnchorZ),lightingRays/CloudVolumeLightingBudget.RaysPerUnit);
        UboPacking.WriteVec4(data,64,(float)CloudVolumeDomain.HorizontalCell,domain.VerticalCell,(float)CloudVolumeDomain.HorizontalCell,densityPending?1:0);
        UboPacking.WriteIVec4(data,80,newlyActive,diffuse?1:0,selectionLevel,CloudVolumeDiffuseProgram.DefaultSlices);
        UboPacking.WriteIVec4(data,96,source,destination,directGroups,diffuseGroups);
        for(int i=0;i<6;i++)UboPacking.WriteVec4(data,112+16*i,planes[i].X,planes[i].Y,planes[i].Z,planes[i].W);
        inputs.SetBytes(data);
    }
    #endregion
    #region Private
    /// <summary>Adopts the pipeline and frozen work parameters.</summary>
    private CloudVolumeWorkProgram(GpuComputePipeline pipeline,PackedUniformBuffer? shared):base(pipeline)
    {inputs=shared??OwnUniformBuffer(new PackedUniformBuffer(208));}
    /// <summary>Publishes the complete work record.</summary>
    CpuUniformBuffer ICloudVolumeWorkBindings.Inputs=>inputs;
    #endregion
}
