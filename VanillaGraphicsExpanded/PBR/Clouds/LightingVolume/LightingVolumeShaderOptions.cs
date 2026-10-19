using VanillaGraphicsExpanded.Rendering.Contracts;

namespace VanillaGraphicsExpanded.PBR.Clouds.LightingVolume;

/// <summary>Defines candidate density selections independently of rendering or diagnostic programs.</summary>
internal static partial class LightingVolumeShaderOptions
{
    #region Public API
    /// <summary>Includes bounded procedural detail and exposed-face erosion only in enabled variants.</summary>
    [ShaderOption("VGE_LIGHTING_VOLUME_DETAIL",true)]
    internal static partial ShaderOption<bool> Detail { get; }
    /// <summary>Includes cached direct-transmission sampling only when self-shadowing is enabled.</summary>
    [ShaderOption("VGE_LIGHTING_VOLUME_DIRECT",true)]
    internal static partial ShaderOption<bool> Direct { get; }
    /// <summary>Includes persistent diffuse sampling only when progressive transport is selected.</summary>
    [ShaderOption("VGE_LIGHTING_VOLUME_DIFFUSE",true)]
    internal static partial ShaderOption<bool> Diffuse { get; }
    /// <summary>Includes the post-step optical exit only when a positive threshold is selected.</summary>
    [ShaderOption("VGE_LIGHTING_VOLUME_TERMINATION",true)]
    internal static partial ShaderOption<bool> Termination { get; }
    #endregion
}
