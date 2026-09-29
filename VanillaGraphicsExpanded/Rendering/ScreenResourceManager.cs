using System;
using System.Collections.Generic;

namespace VanillaGraphicsExpanded.Rendering;

/// <summary>Coordinates screen-sized VGE resources after the engine rebuilds its default framebuffers.</summary>
internal static class ScreenResourceManager
{
    public const int GBufferOrder = 100;
    public const int DirectLightingOrder = 200;
    public const int LumOnOrder = 300;
    public const int CompositeOrder = 400;

    private static readonly SortedDictionary<int, List<Action>> callbacks = [];

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

    public static void HandleScreenResize()
    {
        GlStateCache.Current.InvalidateAll();
        foreach (var orderedCallbacks in callbacks.Values)
        {
            foreach (var callback in orderedCallbacks.ToArray())
            {
                callback();
            }
        }
    }
}