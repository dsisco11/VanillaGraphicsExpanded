using VanillaGraphicsExpanded.Rendering.ShaderCompilation;
using System;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Rendering.Contracts;
using Vintagestory.API.Common;

namespace VanillaGraphicsExpanded.PBR.Clouds.Volume;

/// <summary>Owns one structural generation operation, compiling procedural detail out when disabled.</summary>
[ShaderProgram("Contract","cloud_volume/density",140)]
[ShaderStage("Contract",ShaderStageKind.Compute,"cloud_volume/density.csh")]
[ShaderUse("Contract",ShaderStageKind.Compute,nameof(Operation))]
[ShaderUse("Contract",ShaderStageKind.Compute,nameof(Detail))]
[ShaderUse("Contract",ShaderStageKind.Compute,nameof(Quality))]
internal sealed partial class CloudVolumeDensityProgram : GpuComputeProgram, ICloudVolumeDensityBindings
{
    private readonly CloudVolumeDensityParameters inputs;
    #region Public API
    /// <summary>Selects one compiled workload profile for the entire executable lifetime.</summary>
    [ShaderOptionReference(typeof(CloudVolumeQualityOptions),nameof(CloudVolumeQualityOptions.Level))]
    internal static partial ShaderOption<int> Quality {get;}
    /// <summary>Selects validation, observation, selection, generation, occupancy repair or completion.</summary>
    [ShaderOption("VGE_VOLUME_DENSITY_OPERATION",0,Domain=new object[]{0,1,2,3,4,5,6,7,8,9,10,11,12,13})]
    internal static partial ShaderOption<int> Operation { get; }
    /// <summary>Removes generation-only noise and erosion when disabled.</summary>
    [ShaderOption("VGE_VOLUME_DENSITY_DETAIL",true)]
    internal static partial ShaderOption<bool> Detail { get; }
    /// <summary>Loads only the selected structural operation and density variant.</summary>
    internal static CloudVolumeDensityProgram Create(ICoreAPI api,int operation,bool detail,CloudVolumeDensityParameters? shared=null,int quality=2)
    {
        var layout=new GpuProgramLayout();layout.RegisterContract(Contract.Stages[0].Bindings);
        if(!ComputeShaderAsset.TryCreateFromAssets(api,new ShaderSettings(Contract).With(Operation,operation).With(Detail,detail).With(Quality,quality),
            out var pipeline,out string log,"Cloud.Volume.Density",layout:layout)) throw new InvalidOperationException(log);
        return new(pipeline!,shared);
    }
    /// <summary>Captures relative keys and physical units without persisting a native map handle.</summary>
    internal void Capture(CloudVolumeDomain domain,long x,long z,CloudVolumeDensityState state,uint frame,bool initial,int radixShift=0)
    {
        RequireInputMutation();
        inputs.Capture(domain, x, z, state, frame, initial, radixShift);
    }
    #endregion
    #region Private
    /// <summary>Adopts the pipeline and its bounded uniform record.</summary>
    private CloudVolumeDensityProgram(ComputeShaderAsset pipeline,CloudVolumeDensityParameters? shared):base(pipeline)
    {inputs=shared??OwnUniformBuffer(new CloudVolumeDensityParameters());}
    /// <summary>Publishes the captured generation identity.</summary>
    CpuUniformBuffer ICloudVolumeDensityBindings.Inputs=>inputs;
    #endregion
}
