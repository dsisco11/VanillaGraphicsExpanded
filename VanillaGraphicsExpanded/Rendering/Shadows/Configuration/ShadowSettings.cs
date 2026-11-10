using Newtonsoft.Json;

namespace VanillaGraphicsExpanded.Rendering.Shadows.Configuration;

/// <summary>Owns the implemented shadow input policy; rendering admission is not yet available.</summary>
[JsonObject(MemberSerialization.OptIn)]
public sealed class ShadowSettings
{
    /// <summary>Configurable bounded native publication allowances.</summary>
    [JsonProperty] public ShadowInputSettings Inputs { get; set; } = new();

    #region Public API
    /// <summary>Materializes the input policy under the existing configuration owner.</summary>
    internal void Sanitize()
    {
        Inputs ??= new();
        Inputs.Sanitize();
    }
    #endregion
}
