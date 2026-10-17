using System;

using Vintagestory.API.Common;

namespace VanillaGraphicsExpanded.ModSystems;

/// <summary>Publishes applied configuration and refreshes feature owners after configuration has been sanitized.</summary>
internal static class LiveConfigReload
{
    /// <summary>Signals one complete configuration application, preserving related setting updates as a single snapshot.</summary>
    internal static event Action<ICoreAPI>? Applied;

    /// <summary>Notifies cached configuration consumers before refreshing existing feature owners.</summary>
    public static void NotifyAll(ICoreAPI api)
    {
        Applied?.Invoke(api);
        foreach (ModSystem system in api.ModLoader.Systems)
        {
            if (system is ILiveConfigurable live)
            {
                try
                {
                    live.OnConfigReloaded(api);
                }
                catch (Exception ex)
                {
                    api.Logger.Warning("[VanillaExpanded] Live config reload failed in {0}: {1}", system.GetType().FullName, ex);
                }
            }
        }
    }
}
