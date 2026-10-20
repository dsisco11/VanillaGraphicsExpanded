using VanillaGraphicsExpanded.Rendering.Contracts;

namespace VanillaGraphicsExpanded.PBR.Clouds.Volume;

/// <summary>Defines independently compiled stored-field view paths.</summary>
internal static partial class CloudVolumeViewOptions
{
    #region Public API
    /// <summary>Includes hardware-filtered direct visibility only when selected.</summary>
    [ShaderOption("VGE_STORED_VIEW_SHADOWS",true)] internal static partial ShaderOption<bool> Shadows {get;}
    /// <summary>Includes retained diffuse sampling only when selected.</summary>
    [ShaderOption("VGE_STORED_VIEW_DIFFUSE",true)] internal static partial ShaderOption<bool> Diffuse {get;}
    /// <summary>Includes post-segment optical exit only for a positive configured threshold.</summary>
    [ShaderOption("VGE_STORED_VIEW_TERMINATION",true)] internal static partial ShaderOption<bool> Termination {get;}
    #endregion
}
