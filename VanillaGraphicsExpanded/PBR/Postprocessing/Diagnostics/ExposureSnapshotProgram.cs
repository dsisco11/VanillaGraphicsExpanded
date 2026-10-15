using System.Numerics;
using VanillaGraphicsExpanded.PBR.CameraExposure;
using VanillaGraphicsExpanded.PBR.Postprocessing.LocalExposure;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Rendering.Contracts;
using VanillaGraphicsExpanded.Rendering.Shaders;
using Vintagestory.API.Common;

namespace VanillaGraphicsExpanded.PBR.Postprocessing.Diagnostics;

/// <summary>Samples final-display exposure equations only for an explicitly requested log record.</summary>
[ShaderProgram("Contract", "pbr_exposure_snapshot", 1)]
[ShaderStage("Contract", ShaderStageKind.Compute, "pbr_exposure_snapshot.csh")]
internal sealed partial class ExposureSnapshotProgram : GpuComputeProgram, IExposureSnapshotBindings
{
    private readonly SnapshotInputs inputs;
    private CpuUniformBuffer? frameOverride;
    private GpuShaderStorageBuffer? lightingOverride;
    #region Public API
    /// <summary>Declares the executable through the existing shader asset owner.</summary>
    internal static ExposureSnapshotProgram Create(ICoreAPI api)
    {
        var layout = new GpuProgramLayout();
        layout.RegisterContract(Contract.Stages[0].Bindings);
        return new(GpuComputePipeline.DeclareFromAssets(api, new ShaderSettings(Contract), layout: layout));
    }
    /// <summary>Owns the short-lived diagnostic parameter block.</summary>
    public ExposureSnapshotProgram(GpuComputePipeline pipeline) : base(pipeline)
    {
        inputs = OwnUniformBuffer(new SnapshotInputs());
    }
    /// <summary>Stages local correction and the actually applied manual/automatic camera policy.</summary>
    internal void Capture(LocalExposureParameters local, CameraExposureParameters camera, bool antialias)
    {
        RequireInputMutation();
        inputs.Capture(local, camera, antialias);
    }
    /// <summary>Allows isolated GPU fixtures to supply the same completed view metadata.</summary>
    internal CpuUniformBuffer? FrameOverride
    {
        get => frameOverride;
        set { RequireInputMutation(); frameOverride=value; }
    }
    /// <summary>Allows isolated GPU fixtures to supply frozen physical storage.</summary>
    internal GpuShaderStorageBuffer? LightingOverride
    {
        get => lightingOverride;
        set { RequireInputMutation(); lightingOverride=value; }
    }
    /// <summary>Publishes immutable diagnostic inputs.</summary>
    CpuUniformBuffer IExposureSnapshotBindings.Inputs => inputs;
    /// <summary>Borrows the current view without owning it.</summary>
    CpuUniformBuffer IExposureSnapshotBindings.FrameInputs => FrameOverride ?? VgeFrameRenderer.Current;
    /// <summary>Borrows the matching frozen storage publication.</summary>
    GpuShaderStorageBuffer IPhotometricFrameBindings.FrameLighting => LightingOverride ?? CameraExposureRenderer.FrozenLighting;
    #endregion
    #region Private
    /// <summary>Packs the display-only correction controls into three vector slots.</summary>
    private sealed class SnapshotInputs : CpuUniformBuffer
    {
        #region Public API
        /// <summary>Allocates just the diagnostic controls.</summary>
        internal SnapshotInputs() : base(48) { }
        /// <summary>Retains captured policy rather than reading mutable configuration during dispatch.</summary>
        internal void Capture(LocalExposureParameters local, CameraExposureParameters camera, bool antialias)
        {
            WriteVector4(0, new(local.ShadowStrength, local.HighlightStrength, local.ShadowLimit, local.HighlightLimit));
            WriteVector4(16, new(local.RangeWidth, local.Enabled ? 1 : 0, antialias ? 1 : 0, 0));
            float pivot=camera.DisplayLogMiddleGray;
            float manualTarget = camera.ManualLogGain + camera.Compensation;
            WriteVector4(32, new Vector4(System.Math.Clamp(manualTarget, camera.MinLogGain, camera.MaxLogGain),
                camera.Enabled ? 1 : 0, pivot, manualTarget));
        }
        #endregion
    }
    #endregion
}
