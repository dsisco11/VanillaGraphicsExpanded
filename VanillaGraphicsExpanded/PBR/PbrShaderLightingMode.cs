using System;
namespace VanillaGraphicsExpanded.PBR;

/// <summary>Shares the engine shader generation's lighting mode with deferred composition while reloads are queued.</summary>
internal static class PbrShaderLightingMode
{
    #region Generation state
    /// <summary>Frozen at initial source preparation or reload entry; null starts a fresh asset lifetime.</summary>
    internal static bool? GenerationLumOnEnabled
    {
        get => generationLumOnEnabled;
        set
        {
            if(generationLumOnEnabled==value) return;
            generationLumOnEnabled=value;
            // Resetting an asset lifetime does not choose its mode. Notify once the next generation freezes it.
            if(value.HasValue) EnabledChanged?.Invoke();
        }
    }
    private static bool? generationLumOnEnabled;
    /// <summary>Signals that a shader generation has committed its lighting mode.</summary>
    internal static event Action? EnabledChanged;

    /// <summary>Captures the configured mode once so draws cannot adopt a queued mode before engine shaders do.</summary>
    internal static bool LumOnEnabled => GenerationLumOnEnabled ??= ModSystems.ConfigModSystem.Config.LumOn.Enabled;
    #endregion
}
