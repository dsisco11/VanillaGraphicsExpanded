using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Rendering.Contracts;
using VanillaGraphicsExpanded.Rendering.Shaders;

namespace VanillaGraphicsExpanded.PBR.Clouds;

/// <summary>Owns the history-free cloud executable and its immutable submission parameters.</summary>
[ShaderProgram("Contract", "cloud_volume", 2)]
[ShaderStage("Contract", ShaderStageKind.Vertex, "cloud_volume.vsh")]
[ShaderStage("Contract", ShaderStageKind.Fragment, "cloud_volume.fsh")]
[ShaderUse("Contract",ShaderStageKind.Fragment,nameof(SelfShadowing))]
internal sealed partial class CloudVolumeProgram : GpuProgram, ICloudVolumeBindings
{
    internal readonly CloudVolumeParameters Parameters;
    private VgeFrameUniformBuffer? frame;
    private GpuShaderStorageBuffer? lighting;
    private readonly PackedUniformBuffer traversal;

    #region Public API
    #region Preparation
    /// <summary>Selects the precompiled reference lighting variant before executable preparation.</summary>
    [ShaderOptionReference(typeof(CloudShaderOptions),nameof(CloudShaderOptions.SelfShadowing))]
    internal partial bool SelfShadowing { get; set; }
    /// <summary>Registers the dedicated resource contract and owns its parameter block.</summary>
    public CloudVolumeProgram()
    {
        Parameters = OwnUniformBuffer(new CloudVolumeParameters());
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
    #endregion
    #region Layer scheduling
    /// <summary>Stages only source-certified bounds; unavailable bounds conservatively retain the horizontal domain.</summary>
    internal void SetHierarchy(DynamicTexture2D? bounds)
    {
        RequireInputMutation();
        bool available=bounds?.IsValid==true;
        HeightBounds=available?bounds:null;
        System.Span<byte> bytes=stackalloc byte[16];
        UboPacking.WriteIVec4(bytes,0,available?1:0,available?bounds!.MipLevels-1:0,0,0);
        traversal.SetBytes(bytes);
    }
    /// <summary>Publishes current layer bounds independently of reference lighting and temporal state.</summary>
    CpuUniformBuffer ICloudVolumeBindings.TraversalInputs => traversal;
    /// <summary>Uses the current camera without changing it.</summary>
    CpuUniformBuffer ICloudVolumeBindings.FrameInputs => frame ?? VgeFrameRenderer.Current;
    /// <summary>Publishes the owned submission block.</summary>
    CpuUniformBuffer ICloudVolumeBindings.Inputs => Parameters;
    /// <summary>Encodes against the frozen view publication.</summary>
    GpuShaderStorageBuffer CameraExposure.IPhotometricFrameBindings.FrameLighting => lighting ?? CameraExposure.CameraExposureRenderer.FrozenLighting;
    #endregion
    #endregion
}
