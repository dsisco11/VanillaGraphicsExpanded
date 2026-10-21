using System;
using VanillaGraphicsExpanded.Rendering.Contracts;
using Vintagestory.API.Common;

namespace VanillaGraphicsExpanded.Rendering.Shaders.Fixtures;

/// <summary>Exercises production cloud opportunity indexing and generic jitter through typed GPU resources.</summary>
[ShaderProgram("Contract","tests/cloud-sampling",1)]
[ShaderStage("Contract",ShaderStageKind.Compute,"tests/cloud-sampling.csh")]
internal sealed partial class CloudSamplingComputeShader:GpuComputeProgram,ICloudSamplingComputeBindings
{
    #region Public API
    /// <summary>Loads the bounded sampling probe without a cloud density or lighting fixture.</summary>
    internal static CloudSamplingComputeShader Create(ICoreAPI api)
    {
        var layout=new GpuProgramLayout();layout.RegisterContract(Contract.Stages[0].Bindings);
        if(!GpuComputePipeline.TryCreateFromAssets(api,new ShaderSettings(Contract),out var pipeline,out string log,
            "Tests.Cloud.Sampling",layout:layout))throw new InvalidOperationException(log);
        return new(pipeline!);
    }
    #endregion
    #region Private
    /// <summary>Adopts the ordinary typed compute executable.</summary>
    private CloudSamplingComputeShader(GpuComputePipeline pipeline):base(pipeline){}
    #endregion
}
