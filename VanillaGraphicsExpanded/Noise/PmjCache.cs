using System;
using System.Collections.Concurrent;
using System.Threading;

namespace VanillaGraphicsExpanded.Noise;

public sealed class PmjCache
{
    private readonly ConcurrentDictionary<PmjKey, Lazy<PmjSequence>> sequenceCache = new();

    public int Count => sequenceCache.Count;

    public PmjSequence GetOrCreateSequence(in PmjConfig config)
    {
        return GetOrCreateSequence(config, static c => PmjGenerator.Generate(c));
    }

    public PmjSequence GetOrCreateSequence(in PmjConfig config, Func<PmjConfig, PmjSequence> factory)
    {
        if (factory is null) throw new ArgumentNullException(nameof(factory));

        config.Validate();

        var key = PmjKey.FromConfig(config);

        Lazy<PmjSequence> lazy = sequenceCache.GetOrAdd(
            key,
            static (k, state) => new Lazy<PmjSequence>(
                () => state.factory(state.config),
                LazyThreadSafetyMode.ExecutionAndPublication),
            (config, factory));

        return lazy.Value;
    }

    public bool TryGetSequence(in PmjConfig config, out PmjSequence? sequence)
    {
        config.Validate();

        var key = PmjKey.FromConfig(config);

        if (sequenceCache.TryGetValue(key, out var lazy) && lazy.IsValueCreated)
        {
            sequence = lazy.Value;
            return true;
        }

        sequence = null;
        return false;
    }

    public void Clear() => sequenceCache.Clear();
}
