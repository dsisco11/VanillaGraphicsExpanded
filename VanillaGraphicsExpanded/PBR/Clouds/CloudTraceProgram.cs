using System;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Rendering.Contracts;
using VanillaGraphicsExpanded.Rendering.Shaders;

namespace VanillaGraphicsExpanded.PBR.Clouds;

/// <summary>Owns the cloud reconstruction executable and its immutable submission parameters.</summary>
[ShaderProgram("Contract", "cloud_volume_trace", 2)]
[ShaderStage("Contract", ShaderStageKind.Vertex, "cloud_volume.vsh")]
[ShaderStage("Contract", ShaderStageKind.Fragment, "cloud_volume_trace.fsh")]
[ShaderUse("Contract",ShaderStageKind.Fragment,nameof(SelfShadowing))]
internal sealed partial class CloudTraceProgram : GpuProgram, ICloudTraceBindings
{
    internal readonly CloudVolumeParameters Parameters;
    internal readonly CloudTemporalParameters Temporal;
    private VgeFrameUniformBuffer? frame;
    private GpuShaderStorageBuffer? lighting;
    private readonly PackedUniformBuffer traversal;
    private readonly byte[] traversalBytes = new byte[16];

    #region Public API
    #region Preparation
    /// <summary>Selects the precompiled transport variant before executable preparation.</summary>
    [ShaderOptionReference(typeof(CloudShaderOptions),nameof(CloudShaderOptions.SelfShadowing))]
    internal partial bool SelfShadowing { get; set; }
    /// <summary>Registers the dedicated resource contract and owns its parameter block.</summary>
    public CloudTraceProgram()
    {
        Parameters = OwnUniformBuffer(new CloudVolumeParameters());
        Temporal = OwnUniformBuffer(new CloudTemporalParameters());
        traversal = OwnUniformBuffer(new PackedUniformBuffer(16));
        ProgramLayout.RegisterContract(Contract.Stages[0].Bindings);
        ProgramLayout.RegisterContract(Contract.Stages[1].Bindings);
    }
    #endregion
    #region View and physical inputs
    /// <summary>Identifies the offline volume stages.</summary>
    internal override GpuShaderContract ProgramContract => Contract;
    /// <summary>Accepts a complete alternate camera rather than implicitly reusing main-view transforms.</summary>
    internal VgeFrameUniformBuffer? Frame { get => frame; set { RequireInputMutation(); frame = value; } }
    /// <summary>Accepts matching controlled-view frozen lighting storage.</summary>
    internal GpuShaderStorageBuffer? Lighting { get => lighting; set { RequireInputMutation(); lighting = value; } }
    /// <summary>Uses the current camera without changing it.</summary>
    CpuUniformBuffer ICloudVolumeBindings.FrameInputs => frame ?? VgeFrameRenderer.Current;
    /// <summary>Publishes the owned submission block.</summary>
    CpuUniformBuffer ICloudVolumeBindings.Inputs => Parameters;
    /// <summary>Encodes against the frozen view publication.</summary>
    GpuShaderStorageBuffer CameraExposure.IPhotometricFrameBindings.FrameLighting => lighting ?? CameraExposure.CameraExposureRenderer.FrozenLighting;
    #endregion
    #region Scheduling and traversal
    /// <summary>Publishes owned reprojection parameters.</summary>
    CpuUniformBuffer ICloudTraceBindings.TemporalInputs => Temporal;
    /// <summary>Publishes traversal availability independently of density and temporal controls.</summary>
    CpuUniformBuffer ICloudTraceBindings.TraversalInputs => traversal;
    /// <summary>Stages a certified hierarchy or explicitly disables acceleration for this submission.</summary>
    internal void SetHierarchy(DynamicTexture2D? bounds)
    {
        RequireInputMutation();
        bool available = bounds?.IsValid == true;
        HeightBounds = available ? bounds : null;
        UboPacking.WriteIVec4(traversalBytes, 0, available ? 1 : 0, available ? bounds!.MipLevels - 1 : 0, 0, 0);
        traversal.SetBytes(traversalBytes);
    }
    /// <summary>Publishes only completed same-frame attenuation, or explicitly selects bounded shadow marching.</summary>
    internal void SetShadowCache(DynamicTexture2D? texture,int source)
    {
        RequireInputMutation();
        if(source is <0 or >1) throw new ArgumentOutOfRangeException(nameof(source));
        bool available=texture?.IsValid==true;
        ShadowCache=available?texture:null;
        // Zero disables lookup; one and two identify the selected solar and lunar source.
        UboPacking.WriteIVec4(traversalBytes,0,BitConverter.ToInt32(traversalBytes,0),
            BitConverter.ToInt32(traversalBytes,4),available?source+1:0,0);
        traversal.SetBytes(traversalBytes);
    }
    #endregion
    #endregion
}
