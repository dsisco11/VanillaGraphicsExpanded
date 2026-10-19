using VanillaGraphicsExpanded.Rendering.Contracts;

namespace VanillaGraphicsExpanded.PBR.Clouds.LightingVolume;

/// <summary>Defines candidate density selections independently of rendering or diagnostic programs.</summary>
internal static partial class LightingVolumeShaderOptions
{
    #region Public API
    /// <summary>Includes bounded procedural detail and exposed-face erosion only in enabled variants.</summary>
    [ShaderOption("VGE_LIGHTING_VOLUME_DETAIL",true)]
    internal static partial ShaderOption<bool> Detail { get; }
    #endregion
}
