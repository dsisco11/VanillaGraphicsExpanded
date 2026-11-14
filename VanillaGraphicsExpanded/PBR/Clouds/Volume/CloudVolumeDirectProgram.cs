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
    private readonly CloudVolumeDirectParameters inputs=new();
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
        RequireInputMutation();
        inputs.Capture(d, direction, newlyActive);
    }
    #endregion
    #region Private
    /// <summary>Adopts the executable and its single immutable parameter record.</summary>
    private CloudVolumeDirectProgram(ComputeShaderAsset pipeline):base(pipeline)=>OwnUniformBuffer(inputs);
    /// <summary>Supplies the complete staged transport record.</summary>
    CpuUniformBuffer ICloudVolumeDirectBindings.Inputs=>inputs;
    #endregion
}
