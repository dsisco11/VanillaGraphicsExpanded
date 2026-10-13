using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.PBR.CameraExposure;

namespace VanillaGraphicsExpanded.LumOn;

/// <summary>Supplies the matching physical storage publication for a scene view.</summary>
public partial class LumOnProbeSh9GatherShaderProgram
{
    private GpuShaderStorageBuffer? frameLightingInputs;

    #region Public API
    /// <summary>Borrows an explicit alternate-view publication; normal scene draws use the camera owner's frozen frame.</summary>
    internal GpuShaderStorageBuffer? FrameLightingInputs
    {
        get => frameLightingInputs;
        set { RequireInputMutation(); frameLightingInputs = value; }
    }

    /// <summary>Resolves immutable storage metadata through the same view boundary as the scene's camera inputs.</summary>
    GpuShaderStorageBuffer IPhotometricFrameBindings.FrameLighting => frameLightingInputs ?? CameraExposureRenderer.FrozenLighting;
    #endregion
}

