namespace VanillaGraphicsExpanded.Cache;

public delegate bool TryParseCacheKey<TKey>(string entryId, out TKey key)
    where TKey : notnull;
