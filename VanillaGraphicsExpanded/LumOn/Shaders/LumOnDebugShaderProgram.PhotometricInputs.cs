using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.PBR.CameraExposure;

namespace VanillaGraphicsExpanded.LumOn;

/// <summary>Supplies the originating frame scale for scene-lighting diagnostic views.</summary>
public partial class LumOnDebugShaderProgram
{
    private GpuShaderStorageBuffer? frameLightingInputs;

    #region Public API
    /// <summary>Borrows alternate-view metadata; normal overlays use the current camera publication.</summary>
    internal GpuShaderStorageBuffer? FrameLightingInputs
    {
        get => frameLightingInputs;
        set { RequireInputMutation(); frameLightingInputs = value; }
    }

    /// <summary>Decodes lighting through the same view metadata that produced its diagnostic inputs.</summary>
    GpuShaderStorageBuffer IPhotometricFrameBindings.FrameLighting => frameLightingInputs ?? CameraExposureRenderer.FrozenLighting;
    #endregion
}
