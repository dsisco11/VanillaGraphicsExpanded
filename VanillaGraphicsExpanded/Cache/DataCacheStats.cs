using System;

namespace VanillaGraphicsExpanded.Cache;

public readonly record struct DataCacheStats(
    long Hits,
    long Misses,
    long Stores,
    long DecodeFailures,
    long EncodeFailures,
    long StoreReadFailures,
    long StoreWriteFailures);
