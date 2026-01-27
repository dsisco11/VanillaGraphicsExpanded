using System;
using System.Collections.Generic;

namespace VanillaGraphicsExpanded.Cache;

public interface ICacheStore
{
    /// <summary>
    /// Enumerates entry identifiers currently known to the store.
    /// </summary>
    IEnumerable<string> EnumerateEntryIds();

    bool Contains(string entryId);

    bool TryRead(string entryId, out byte[] bytes);

    bool TryWriteAtomic(string entryId, ReadOnlySpan<byte> bytes);

    bool TryRemove(string entryId);

    void Clear();
}
