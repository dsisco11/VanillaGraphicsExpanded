using System;
using System.Collections.Generic;
using System.Threading;

namespace VanillaGraphicsExpanded.Cache;

public sealed class DataCacheSystem<TKey, TPayload> : IDataCacheSystem<TKey, TPayload>
    where TKey : notnull
{
    private readonly ICacheStore store;
    private readonly ICacheCodec<TPayload> codec;
    private readonly Func<TKey, string> keyToEntryId;
    private readonly TryParseCacheKey<TKey> tryParseKey;

    private long hits;
    private long misses;
    private long stores;
    private long decodeFailures;
    private long encodeFailures;
    private long storeReadFailures;
    private long storeWriteFailures;

    public DataCacheSystem(
        ICacheStore store,
        ICacheCodec<TPayload> codec,
        Func<TKey, string> keyToEntryId,
        TryParseCacheKey<TKey> tryParseKey)
    {
        this.store = store ?? throw new ArgumentNullException(nameof(store));
        this.codec = codec ?? throw new ArgumentNullException(nameof(codec));
        this.keyToEntryId = keyToEntryId ?? throw new ArgumentNullException(nameof(keyToEntryId));
        this.tryParseKey = tryParseKey ?? throw new ArgumentNullException(nameof(tryParseKey));
    }

    public bool TryGet(TKey key, out TPayload payload)
    {
        string entryId;
        try
        {
            entryId = keyToEntryId(key);
        }
        catch
        {
            payload = default!;
            Interlocked.Increment(ref misses);
            return false;
        }

        if (string.IsNullOrWhiteSpace(entryId))
        {
            payload = default!;
            Interlocked.Increment(ref misses);
            return false;
        }

        if (!store.TryRead(entryId, out byte[] bytes))
        {
            payload = default!;
            Interlocked.Increment(ref misses);
            Interlocked.Increment(ref storeReadFailures);
            return false;
        }

        if (!codec.TryDecode(bytes, out payload))
        {
            payload = default!;
            Interlocked.Increment(ref misses);
            Interlocked.Increment(ref decodeFailures);
            return false;
        }

        Interlocked.Increment(ref hits);
        return true;
    }

    public void Store(TKey key, in TPayload payload)
    {
        string entryId;
        try
        {
            entryId = keyToEntryId(key);
        }
        catch
        {
            Interlocked.Increment(ref storeWriteFailures);
            return;
        }

        if (string.IsNullOrWhiteSpace(entryId))
        {
            Interlocked.Increment(ref storeWriteFailures);
            return;
        }

        if (!codec.TryEncode(payload, out byte[] bytes))
        {
            Interlocked.Increment(ref encodeFailures);
            return;
        }

        if (!store.TryWriteAtomic(entryId, bytes))
        {
            Interlocked.Increment(ref storeWriteFailures);
            return;
        }

        Interlocked.Increment(ref stores);
    }

    public bool Remove(TKey key)
    {
        string entryId;
        try
        {
            entryId = keyToEntryId(key);
        }
        catch
        {
            return false;
        }

        if (string.IsNullOrWhiteSpace(entryId))
        {
            return false;
        }

        return store.TryRemove(entryId);
    }

    public void Clear() => store.Clear();

    public DataCacheStats GetStatsSnapshot()
        => new(
            Hits: Interlocked.Read(ref hits),
            Misses: Interlocked.Read(ref misses),
            Stores: Interlocked.Read(ref stores),
            DecodeFailures: Interlocked.Read(ref decodeFailures),
            EncodeFailures: Interlocked.Read(ref encodeFailures),
            StoreReadFailures: Interlocked.Read(ref storeReadFailures),
            StoreWriteFailures: Interlocked.Read(ref storeWriteFailures));

    public int CountExisting(IReadOnlyList<TKey> keys)
    {
        ArgumentNullException.ThrowIfNull(keys);

        if (keys.Count == 0)
        {
            return 0;
        }

        int hits = 0;
        for (int i = 0; i < keys.Count; i++)
        {
            string entryId;
            try
            {
                entryId = keyToEntryId(keys[i]);
            }
            catch
            {
                continue;
            }

            if (string.IsNullOrWhiteSpace(entryId))
            {
                continue;
            }

            if (store.Contains(entryId))
            {
                hits++;
            }
        }

        return hits;
    }

    public IEnumerable<TKey> EnumerateCachedKeys()
    {
        foreach (string entryId in store.EnumerateEntryIds())
        {
            if (tryParseKey(entryId, out TKey key))
            {
                yield return key;
            }
        }
    }
}
