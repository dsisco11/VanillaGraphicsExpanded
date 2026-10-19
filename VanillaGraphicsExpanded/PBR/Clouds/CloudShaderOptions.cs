using VanillaGraphicsExpanded.Rendering.Contracts;

namespace VanillaGraphicsExpanded.PBR.Clouds;

/// <summary>Declares compile-time cloud transport selections shared by reference and reduced tracing.</summary>
internal static partial class CloudShaderOptions
{
    #region Public API
    /// <summary>Includes cloud-internal attenuation only in enabled transport variants.</summary>
    [ShaderOption("VGE_CLOUD_SELF_SHADOWING",true)]
    internal static partial ShaderOption<bool> SelfShadowing { get; }
    #endregion
}
