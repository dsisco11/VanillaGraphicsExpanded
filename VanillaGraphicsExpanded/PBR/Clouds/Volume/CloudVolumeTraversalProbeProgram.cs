using VanillaGraphicsExpanded.Rendering.ShaderCompilation;
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
    private readonly CloudVolumeTraversalProbeParameters inputs=new();
    #region Public API
    /// <summary>Loads the one diagnostic traversal shader.</summary>
    internal static CloudVolumeTraversalProbeProgram Create(ICoreAPI api)
    {
        var layout=new GpuProgramLayout();layout.RegisterContract(Contract.Stages[0].Bindings);
        if(!ComputeShaderAsset.TryCreateFromAssets(api,new ShaderSettings(Contract),out var pipeline,out string log,
            "Cloud.Volume.TraversalProbe",layout:layout))throw new InvalidOperationException(log);
        return new(pipeline!);
    }
    /// <summary>Captures native-local geometry and optional deliberately reduced test operation cap.</summary>
    internal void Capture(CloudVolumeDomain d,Vector3 origin,Vector3 direction,float length,int limit=0)
    {
        RequireInputMutation();
        inputs.Capture(d, origin, direction, length, limit);
    }
    #endregion
    #region Private
    /// <summary>Adopts the executable and its authored query.</summary>
    private CloudVolumeTraversalProbeProgram(ComputeShaderAsset pipeline):base(pipeline)=>OwnUniformBuffer(inputs);
    /// <summary>Publishes the staged bounded diagnostic record.</summary>
    CpuUniformBuffer ICloudVolumeTraversalProbeBindings.Inputs=>inputs;
    #endregion
}
