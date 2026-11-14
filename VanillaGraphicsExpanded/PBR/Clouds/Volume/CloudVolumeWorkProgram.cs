using VanillaGraphicsExpanded.Rendering.ShaderCompilation;
using System;
using System.Numerics;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Rendering.Contracts;
using Vintagestory.API.Common;

namespace VanillaGraphicsExpanded.PBR.Clouds.Volume;

/// <summary>Selects or completes bounded lighting work through independently compiled structural operations.</summary>
[ShaderProgram("Contract","cloud_volume/work",110)]
[ShaderStage("Contract",ShaderStageKind.Compute,"cloud_volume/work.csh")]
[ShaderUse("Contract",ShaderStageKind.Compute,nameof(Operation))]
[ShaderUse("Contract",ShaderStageKind.Compute,nameof(LargeBudget))]
[ShaderUse("Contract",ShaderStageKind.Compute,nameof(Quality))]
internal sealed partial class CloudVolumeWorkProgram : GpuComputeProgram,ICloudVolumeWorkBindings
{
    private readonly CloudVolumeWorkParameters inputs;
    #region Public API
    /// <summary>Selects one compiled workload profile for the entire executable lifetime.</summary>
    [ShaderOptionReference(typeof(CloudVolumeQualityOptions),nameof(CloudVolumeQualityOptions.Level))]
    internal static partial ShaderOption<int> Quality {get;}
    /// <summary>Selects list production or successful-only completion metadata writes.</summary>
    [ShaderOption("VGE_VOLUME_WORK_OPERATION",0,Domain=new object[]{0,1,2,3,4,5,6,7,8,9,10})]
    internal static partial ShaderOption<int> Operation { get; }
    /// <summary>Expands shared storage when bounded occupied-origin refill needs more than 64 candidate jobs.</summary>
    [ShaderOption("VGE_VOLUME_WORK_LARGE_BUDGET",false)]
    internal static partial ShaderOption<bool> LargeBudget {get;}
    /// <summary>Loads only the selected work executable.</summary>
    internal static CloudVolumeWorkProgram Create(ICoreAPI api,int operation,bool largeBudget=false,CloudVolumeWorkParameters? shared=null,int quality=2)
    {
        var layout=new GpuProgramLayout();layout.RegisterContract(Contract.Stages[0].Bindings);
        if(!ComputeShaderAsset.TryCreateFromAssets(api,new ShaderSettings(Contract).With(Operation,operation).With(LargeBudget,largeBudget).With(Quality,quality),out var pipeline,
            out string log,"Cloud.Volume.Work",layout:layout))throw new InvalidOperationException(log);
        return new(pipeline!,shared);
    }
    /// <summary>Stages one immutable list identity and region bounds; completion uses the same snapshot.</summary>
    internal void Capture(CloudVolumeDomain domain,uint frame,uint revision,int active,int dirty,int newlyActive,
        bool diffuse,int completed,ReadOnlySpan<Vector4> planes,int source=0,int destination=0,int directGroups=0,int diffuseGroups=0,
        bool densityPending=false,int lightingRays=CloudVolumeLightingBudget.DefaultRays,int selectionLevel=0)
    {
        RequireInputMutation();
        inputs.Capture(domain, CloudVolumeDiffuseProgram.DefaultSlices, frame, revision, active, dirty, newlyActive, diffuse, completed, planes, source, destination, directGroups, diffuseGroups, densityPending, lightingRays, selectionLevel);
    }
    #endregion
    #region Private
    /// <summary>Adopts the pipeline and frozen work parameters.</summary>
    private CloudVolumeWorkProgram(ComputeShaderAsset pipeline,CloudVolumeWorkParameters? shared):base(pipeline)
    {inputs=shared??OwnUniformBuffer(new CloudVolumeWorkParameters());}
    /// <summary>Publishes the complete work record.</summary>
    CpuUniformBuffer ICloudVolumeWorkBindings.Inputs=>inputs;
    #endregion
}
