using VanillaGraphicsExpanded.Rendering.ShaderCompilation;
using System;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Rendering.Contracts;
using Vintagestory.API.Common;

namespace VanillaGraphicsExpanded.PBR.Clouds.Volume;

/// <summary>Gathers persistent diffuse illumination with independently compiled self-shadowing.</summary>
[ShaderProgram("Contract","cloud_volume/diffuse",10)]
[ShaderStage("Contract",ShaderStageKind.Compute,"cloud_volume/diffuse.csh")]
[ShaderUse("Contract",ShaderStageKind.Compute,nameof(SelfShadow))]
[ShaderUse("Contract",ShaderStageKind.Compute,nameof(Quality))]
internal sealed partial class CloudVolumeDiffuseProgram:GpuComputeProgram,ICloudVolumeDiffuseBindings
{
    internal const int DefaultSlices=8;
    private readonly CloudVolumeDiffuseParameters inputs=new();
    #region Public API
    /// <summary>Selects one compiled workload profile for the entire executable lifetime.</summary>
    [ShaderOptionReference(typeof(CloudVolumeQualityOptions),nameof(CloudVolumeQualityOptions.Level))]
    internal static partial ShaderOption<int> Quality {get;}
    /// <summary>Removes the direct-visibility sampler and lookup when self-shadowing is disabled.</summary>
    [ShaderOption("VGE_VOLUME_DIFFUSE_SHADOWS",true)]
    internal static partial ShaderOption<bool> SelfShadow {get;}
    /// <summary>Loads the independently selected transport variant.</summary>
    internal static CloudVolumeDiffuseProgram Create(ICoreAPI api,bool shadows,int quality=2)
    {
        var layout=new GpuProgramLayout();layout.RegisterContract(Contract.Stages[0].Bindings);
        if(!ComputeShaderAsset.TryCreateFromAssets(api,new ShaderSettings(Contract).With(SelfShadow,shadows).With(Quality,quality),out var pipeline,
            out string log,"Cloud.Volume.Diffuse",layout:layout))throw new InvalidOperationException(log);
        return new(pipeline!);
    }
    /// <summary>Stages the frozen physical scene, geometry and coherent source/destination encodings.</summary>
    internal void Capture(CloudVolumeWorkBatch batch,CloudVolumeEncoding target)
    {
        RequireInputMutation();
        inputs.Capture(batch, target);
    }
    #endregion
    #region Private
    /// <summary>Owns the executable's single packed transport record.</summary>
    private CloudVolumeDiffuseProgram(ComputeShaderAsset pipeline):base(pipeline)=>OwnUniformBuffer(inputs);
    /// <summary>Supplies the complete staged gather contract.</summary>
    CpuUniformBuffer ICloudVolumeDiffuseBindings.Inputs=>inputs;
    #endregion
}
