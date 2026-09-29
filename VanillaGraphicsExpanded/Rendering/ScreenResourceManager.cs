using System;
using System.Collections.Generic;

namespace VanillaGraphicsExpanded.Rendering;

/// <summary>Coordinates screen-sized resources after the engine publishes new framebuffers and retires the old ones.</summary>
internal static class ScreenResourceManager
{
    public const int GBufferOrder = 100;
    public const int DirectLightingOrder = 200;
    public const int LumOnOrder = 300;
    public const int CompositeOrder = 400;

    private static readonly SortedDictionary<int, List<Action>> callbacks = [];

    #region Resource lifecycle
    /// <summary>Registers a render-thread resize callback in dependency order and returns its unregistration action.</summary>
    public static Action Register(int order, Action callback)
    {
        ArgumentNullException.ThrowIfNull(callback);
        if (!callbacks.TryGetValue(order, out var orderedCallbacks))
        {
            orderedCallbacks = [];
            callbacks.Add(order, orderedCallbacks);
        }

        orderedCallbacks.Add(callback);
        return () => orderedCallbacks.Remove(callback);
    }

    /// <summary>Reattaches and resizes resources after the complete engine framebuffer rebuild.</summary>
    public static void HandleScreenResize()
    {
        // Engine retirement deletes cached framebuffer and texture bindings. Invalidate after
        // that deletion, before any resource callback can save and restore those bindings.
        GlStateCache.Current.InvalidateAll();
        foreach (var orderedCallbacks in callbacks.Values)
        {
            foreach (var callback in orderedCallbacks.ToArray())
            {
                callback();
            }
        }
    }
    #endregion
}
