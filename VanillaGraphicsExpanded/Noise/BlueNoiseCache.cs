using System;
using System.Collections.Concurrent;
using System.Threading;

namespace VanillaGraphicsExpanded.Noise;

public sealed class BlueNoiseCache
{
    private readonly ConcurrentDictionary<BlueNoiseKey, Lazy<BlueNoiseRankMap>> rankCache = new();

    public int Count => rankCache.Count;

    public BlueNoiseRankMap GetOrCreateRankMap(in BlueNoiseConfig config, Func<BlueNoiseConfig, BlueNoiseRankMap> factory)
    {
        if (factory is null) throw new ArgumentNullException(nameof(factory));

        config.Validate();

        var key = BlueNoiseKey.FromConfig(config);

        Lazy<BlueNoiseRankMap> lazy = rankCache.GetOrAdd(
            key,
            static (k, state) => new Lazy<BlueNoiseRankMap>(
                () => state.factory(state.config),
                LazyThreadSafetyMode.ExecutionAndPublication),
            (config, factory));

        return lazy.Value;
    }

    public bool TryGetRankMap(in BlueNoiseConfig config, out BlueNoiseRankMap? map)
    {
        config.Validate();

        var key = BlueNoiseKey.FromConfig(config);

        if (rankCache.TryGetValue(key, out var lazy) && lazy.IsValueCreated)
        {
            map = lazy.Value;
            return true;
        }

        map = null;
        return false;
    }

    public void Clear() => rankCache.Clear();
}
