using System;
using VanillaGraphicsExpanded.PBR.Clouds.LightingVolume;

namespace VanillaGraphicsExpanded.PBR.Clouds;

/// <summary>Stages configuration until early preparation, preserving the current frame's draw owner.</summary>
internal sealed class CloudVolumeSelection
{
    private CloudVolumeRendererKind pending;
    private LightingVolumeViewSettings pendingSettings=LightingVolumeViewSettings.Default;
    internal CloudVolumeRendererKind Renderer { get; private set; }
    internal LightingVolumeViewSettings Settings { get; private set; }=LightingVolumeViewSettings.Default;
    #region Public API
    /// <summary>Captures applied controls without changing admitted draw ownership.</summary>
    internal void Request(CloudRenderingSettings config)
    {
        config.Sanitize();pending=config.VolumeRenderer;pendingSettings=config.LightingVolume.Capture();
    }
    /// <summary>Retires an incompatible renderer before exposing its replacement to preparation.</summary>
    internal void Apply(Action retire)
    {
        if(Renderer!=pending) retire();
        Renderer=pending;Settings=pendingSettings;
    }
    #endregion
}
