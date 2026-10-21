using VanillaGraphicsExpanded.Rendering.Contracts;

namespace VanillaGraphicsExpanded.PBR.Clouds.Volume;

/// <summary>Compiles optional history diagnostics independently of physical temporal transport.</summary>
internal static partial class CloudVolumeTemporalOptions
{
    #region Public API
    /// <summary>Includes contribution receipts and visualizer composition only when requested.</summary>
    [ShaderOption("VGE_CLOUD_TEMPORAL_DIAGNOSTICS",false)]
    internal static partial ShaderOption<bool> Diagnostics {get;}
    #endregion
}
