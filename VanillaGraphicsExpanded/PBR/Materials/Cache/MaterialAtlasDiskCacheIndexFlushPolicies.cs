using System;

namespace VanillaGraphicsExpanded.PBR.Materials.Cache;

internal static class MaterialAtlasDiskCacheIndexFlushPolicies
{
    public static IMaterialAtlasDiskCacheIndexFlushPolicy Default { get; } = new DebouncedPolicy(TimeSpan.FromMilliseconds(250));

    public static IMaterialAtlasDiskCacheIndexFlushPolicy Synchronous { get; } = new SynchronousPolicy();

    private sealed class DebouncedPolicy : IMaterialAtlasDiskCacheIndexFlushPolicy
    {
        public DebouncedPolicy(TimeSpan debounceDelay)
        {
            DebounceDelay = debounceDelay < TimeSpan.Zero ? TimeSpan.Zero : debounceDelay;
        }

        public bool FlushSynchronously => false;

        public TimeSpan DebounceDelay { get; }
    }

    private sealed class SynchronousPolicy : IMaterialAtlasDiskCacheIndexFlushPolicy
    {
        public bool FlushSynchronously => true;

        public TimeSpan DebounceDelay => TimeSpan.Zero;
    }
}
