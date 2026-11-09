using VanillaGraphicsExpanded.Tests.Support.Cache;
using VanillaGraphicsExpanded.Rendering.ProgramBinaries;
namespace VanillaGraphicsExpanded.Tests.Component.Rendering.Shaders;

/// <summary>Exercises ProgramBinaryStore persistence, corruption recovery, budgets, and lock contention over a controlled in-memory filesystem.</summary>
public sealed class ProgramBinaryStoreTests : IDisposable
{
    #region Public API
    #region Storage behavior
    /// <summary>Preserves format and bytes through a new store owner on the in-memory filesystem.</summary>
    [Fact]
    public void RoundTripSurvivesNewOwnerAndRemoval()
    {
        // Arrange an empty store with an isolated in-memory filesystem.
        var store = new ProgramBinaryStore(directory, fileSystem: storage);

        // Act and assert an empty cache reports a miss.
        Assert.False(store.TryRead(First, out _, out _));

        // Act by writing the entry and reopening the same cache path.
        store.Write(First, 17, [1, 3, 7, 9]);
        var reopened = new ProgramBinaryStore(directory, fileSystem: storage);

        // Assert the reopened entry retains its exact format and bytes.
        Assert.True(reopened.TryRead(First, out int format, out byte[] data));
        Assert.Equal(17, format);
        Assert.Equal(new byte[] { 1, 3, 7, 9 }, data);

        // Act and assert removing the entry returns the cache to a miss.
        reopened.Remove(First);
        Assert.False(reopened.TryRead(First, out _, out _));
    }

    /// <summary>A damaged index is a miss and does not prevent rebuilding the disposable cache.</summary>
    [Fact]
    public void CorruptIndexFallsBackAndCanBeRepopulated()
    {
        // Arrange a valid entry, then replace its disposable index with malformed text.
        var store = new ProgramBinaryStore(directory, fileSystem: storage);
        store.Write(First, 1, [1, 2, 3]);
        string index = Assert.Single(storage.EnumerateFiles(directory, "*.json"));
        storage.WriteAllText(index, "not json");
        var reopened = new ProgramBinaryStore(directory, fileSystem: storage);

        // Act and assert the damaged index is treated as a miss.
        Assert.False(reopened.TryRead(First, out _, out _));

        // Act by writing a new entry after the damaged index is discarded.
        reopened.Write(Second, 2, [4, 5]);

        // Assert the corrupt entry is a miss and the replacement remains readable.
        Assert.True(reopened.TryRead(Second, out int format, out byte[] data));
        Assert.Equal(2, format);
        Assert.Equal(new byte[] { 4, 5 }, data);
    }

    /// <summary>A single-entry budget evicts the older executable while retaining the new one.</summary>
    [Fact]
    public void EntryBudgetIsEnforced()
    {
        // Arrange a store limited to one entry.
        var store = new ProgramBinaryStore(directory, maxEntries: 1, fileSystem: storage);

        // Act by writing two distinct binary keys.
        store.Write(First, 1, [1, 2]);
        store.Write(Second, 2, [3, 4]);

        // Assert the older entry is evicted while the newer one remains.
        Assert.False(store.TryRead(First, out _, out _));
        Assert.True(store.TryRead(Second, out _, out _));
    }





    /// <summary>Truncated or tampered payloads and missing files cannot be returned to the driver.</summary>
    [Fact]
    public void PayloadIntegrityAndByteBudgetAreEnforced()
    {
        // Arrange a store whose byte budget is smaller than the first candidate.
        var store = new ProgramBinaryStore(directory, maxBytes: 4, fileSystem: storage);

        // Act with an oversized payload and assert it cannot be read.
        store.Write(First, 1, [1, 2, 3, 4, 5]);
        Assert.False(store.TryRead(First, out _, out _));

        // Act with a valid payload, tamper with its bytes, and assert the digest mismatch is rejected.
        store.Write(First, 1, [1, 2, 3, 4]);
        string file = Assert.Single(storage.EnumerateFiles(directory, "*.bin"));
        storage.WriteAllBytes(file, [1, 2, 3, 5]);
        Assert.False(store.TryRead(First, out _, out _));

        // Act by writing a valid payload and removing it; assert a missing payload is rejected.
        store.Write(First, 1, [1, 2, 3, 4]);
        storage.DeleteFile(file);

        // Assert tampered and missing payloads are both rejected.
        Assert.False(store.TryRead(First, out _, out _));
    }

    /// <summary>Unavailable storage is optional and never escapes into shader loading.</summary>
    [Fact]
    public void FileInsteadOfDirectoryIsHarmless()
    {
        // Arrange a path occupied by a file instead of a writable cache directory.
        storage.CreateDirectory(directory);
        string path = Path.Combine(directory, "blocked");
        storage.WriteAllText(path, "file");
        var store = new ProgramBinaryStore(path, fileSystem: storage);

        // Act on the unavailable cache and assert it degrades to a miss.
        store.Write(First, 1, [1]);
        Assert.False(store.TryRead(First, out _, out _));
        store.Remove(First);
    }

    /// <summary>A held in-memory lease makes caching a miss without blocking later recovery.</summary>
    [Fact]
    public void ContendedLeaseSkipsStorageAndRecovers()
    {
        // Arrange an existing cache and hold its exclusive lease from another owner.
        storage.CreateDirectory(directory);
        var store = new ProgramBinaryStore(directory, fileSystem: storage);
        using (var lease = storage.AcquireExclusive(Path.Combine(directory, "index.lock")))
        {
            // Act and assert a write while locked remains a miss, then remove the uncommitted candidate.
            store.Write(First, 1, [1, 2]);
            Assert.False(store.TryRead(First, out _, out _));
            store.Remove(First);
        }

        // Act after releasing the competing lease and assert the same entry can be stored.
        store.Write(First, 1, [1, 2]);
        Assert.True(store.TryRead(First, out _, out _));
    }

    /// <summary>Repeated retirement of an old lease cannot release a later owner's exclusion.</summary>
    [Fact]
    public void RetiredLeaseCannotReleaseItsReplacement()
    {
        // Arrange a replacement owner after retiring the first lease.
        string path = Path.Combine(directory, "index.lock");
        using var first = storage.AcquireExclusive(path);
        first.Dispose();
        using var replacement = storage.AcquireExclusive(path);
        first.Dispose();

        // Act through a write attempt and assert the replacement lease still excludes access.
        var store = new ProgramBinaryStore(directory, fileSystem: storage);
        store.Write(First, 1, [1, 2]);
        Assert.False(store.TryRead(First, out _, out _));
        Assert.Throws<IOException>(() => storage.AcquireExclusive(path));

        // Act after the replacement retires and assert cache writes work again.
        replacement.Dispose();
        store.Write(First, 1, [1, 2]);
        Assert.True(store.TryRead(First, out _, out _));
    }

    /// <summary>Cache deletion is confined to the temporary directory created by this test.</summary>
    public void Dispose()
    {
        if (storage.DirectoryExists(directory)) storage.DeleteDirectory(directory, recursive: true);
    }
    #endregion
    #endregion

    #region Private
    private readonly CountingDiskCacheFileSystem storage = new();
    private readonly string directory = Path.Combine(Path.GetTempPath(), "VGE.ProgramBinaryTests", Guid.NewGuid().ToString("N"));
    private static readonly string First = new('a', 64), Second = new('b', 64);
    #endregion

}
