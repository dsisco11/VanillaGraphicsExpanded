using System;
using System.Collections.Generic;

namespace VanillaGraphicsExpanded.Cache;

public interface IDataCacheSystem<TKey, TPayload>
    where TKey : notnull
{
    bool TryGet(TKey key, out TPayload payload);

    void Store(TKey key, in TPayload payload);

    bool Remove(TKey key);

    void Clear();

    DataCacheStats GetStatsSnapshot();

    IEnumerable<TKey> EnumerateCachedKeys();
}

