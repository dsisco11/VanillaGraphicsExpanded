namespace VanillaGraphicsExpanded.PBR.Clouds;

/// <summary>Stages applied configuration until preparation without changing a frozen frame's settings.</summary>
internal sealed class CloudVolumeConfiguration
{
    private Volume.CloudVolumeViewSettings pending=Volume.CloudVolumeViewSettings.Default;
    /// <summary>Exposes the coherent settings admitted at the latest preparation boundary.</summary>
    internal Volume.CloudVolumeViewSettings Settings {get;private set;}=Volume.CloudVolumeViewSettings.Default;
    #region Public API
    /// <summary>Captures a complete applied configuration without changing active rendering.</summary>
    internal void Request(CloudRenderingSettings config)=>pending=config.Capture();
    /// <summary>Publishes the captured settings only at the early preparation boundary.</summary>
    internal void Apply()=>Settings=pending;
    #endregion
}
