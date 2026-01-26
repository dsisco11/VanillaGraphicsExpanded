using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace VanillaGraphicsExpanded.Cache.Disk;

public sealed class DiskBlobCacheStore : ICacheStore, IDisposable
{
    private const string MetaFileName = "meta.json";

    private static readonly TimeSpan DefaultDebounceDelay = TimeSpan.FromMilliseconds(250);

    private readonly IDiskCacheFileSystem fileSystem;
    private readonly string root;
    private readonly string payloadDir;
    private readonly string metaPath;

    private readonly object gate = new();
    private readonly DebouncedFlushScheduler flush;
    private bool dirty;

    private DiskCacheIndex index;

    public DiskBlobCacheStore(string rootDirectory)
        : this(rootDirectory, fileSystem: null)
    {
    }

    internal DiskBlobCacheStore(string rootDirectory, IDiskCacheFileSystem? fileSystem, TimeSpan? debounceDelay = null)
    {
        if (string.IsNullOrWhiteSpace(rootDirectory)) throw new ArgumentException("Root directory must be provided", nameof(rootDirectory));

        this.fileSystem = fileSystem ?? new RealDiskCacheFileSystem();
        root = rootDirectory;
        payloadDir = Path.Combine(root, "payloads");
        metaPath = Path.Combine(root, MetaFileName);

        flush = new DebouncedFlushScheduler(FlushIndexBestEffort, debounceDelay ?? DefaultDebounceDelay);

        EnsureDirs();
        index = LoadOrCreateIndex();
    }

    // Test hook: allow providing a custom filesystem from tests.
    internal DiskBlobCacheStore(string rootDirectory, IDiskCacheFileSystem fileSystem)
        : this(rootDirectory, (IDiskCacheFileSystem?)fileSystem, debounceDelay: null)
    {
    }

    // Note: debounceDelay can be provided via the primary internal ctor.

    public void Dispose()
    {
        try
        {
            FlushIndexBestEffort();
        }
        catch
        {
            // Best-effort.
        }

        flush.Dispose();
    }

    public IEnumerable<string> EnumerateEntryIds()
    {
        string[] keys;
        lock (gate)
        {
            keys = index.Entries.Keys.ToArray();
        }

        foreach (string key in keys)
        {
            yield return key;
        }
    }

    public bool TryRead(string entryId, out byte[] bytes)
    {
        bytes = Array.Empty<byte>();

        if (string.IsNullOrWhiteSpace(entryId))
        {
            return false;
        }

        lock (gate)
        {
            if (!index.Entries.ContainsKey(entryId))
            {
                return false;
            }
        }

        string payloadPath = GetPayloadPath(entryId);
        try
        {
            if (!fileSystem.FileExists(payloadPath))
            {
                TryRemove(entryId);
                return false;
            }

            bytes = fileSystem.ReadAllBytes(payloadPath);
            Touch(entryId, bytes.LongLength);
            return true;
        }
        catch
        {
            TryRemove(entryId);
            return false;
        }
    }

    public bool TryWriteAtomic(string entryId, ReadOnlySpan<byte> bytes)
    {
        if (string.IsNullOrWhiteSpace(entryId))
        {
            return false;
        }

        try
        {
            EnsureDirs();

            string payloadPath = GetPayloadPath(entryId);
            if (!AtomicDiskFile.TryWriteAtomic(fileSystem, payloadPath, bytes))
            {
                return false;
            }

            long now = DateTime.UtcNow.Ticks;
            lock (gate)
            {
                if (!index.Entries.TryGetValue(entryId, out DiskCacheIndex.Entry existing))
                {
                    index.Entries[entryId] = new DiskCacheIndex.Entry(
                        SizeBytes: bytes.Length,
                        CreatedUtcTicks: now,
                        LastAccessUtcTicks: now);
                }
                else
                {
                    index.Entries[entryId] = existing with
                    {
                        SizeBytes = bytes.Length,
                        LastAccessUtcTicks = now,
                    };
                }

                dirty = true;
            }

            // Debounced index save to avoid meta.json spam under heavy write load.
            flush.Request();
            return true;
        }
        catch
        {
            return false;
        }
    }

    public bool TryRemove(string entryId)
    {
        if (string.IsNullOrWhiteSpace(entryId))
        {
            return false;
        }

        bool changed = false;

        lock (gate)
        {
            if (index.Entries.Remove(entryId))
            {
                changed = true;
                dirty = true;
            }
        }

        try
        {
            fileSystem.DeleteFile(GetPayloadPath(entryId));
        }
        catch
        {
            // Best-effort.
        }

        if (changed)
        {
            flush.Request();
        }

        return changed;
    }

    public void Clear()
    {
        try
        {
            if (fileSystem.DirectoryExists(root))
            {
                fileSystem.DeleteDirectory(root, recursive: true);
            }
        }
        catch
        {
            // Best-effort.
        }

        EnsureDirs();
        lock (gate)
        {
            index = DiskCacheIndex.CreateEmpty(DateTime.UtcNow.Ticks);
            dirty = true;
        }

        flush.Request();
    }

    private void EnsureDirs()
    {
        if (!fileSystem.DirectoryExists(root))
        {
            fileSystem.CreateDirectory(root);
        }

        if (!fileSystem.DirectoryExists(payloadDir))
        {
            fileSystem.CreateDirectory(payloadDir);
        }
    }

    private DiskCacheIndex LoadOrCreateIndex()
    {
        if (DiskCacheIndex.TryLoad(fileSystem, metaPath, out DiskCacheIndex loaded))
        {
            return loaded;
        }

        var empty = DiskCacheIndex.CreateEmpty(DateTime.UtcNow.Ticks);
        _ = empty.SaveAtomic(fileSystem, metaPath);
        return empty;
    }

    private void Touch(string entryId, long sizeBytes)
    {
        lock (gate)
        {
            long now = DateTime.UtcNow.Ticks;
            if (index.Entries.TryGetValue(entryId, out DiskCacheIndex.Entry entry))
            {
                index.Entries[entryId] = entry with
                {
                    SizeBytes = sizeBytes,
                    LastAccessUtcTicks = now,
                };

                dirty = true;
            }
        }

        flush.Request();
    }

    private void FlushIndexBestEffort()
    {
        lock (gate)
        {
            if (!dirty)
            {
                return;
            }

            dirty = false;
        }

        if (!SaveIndexBestEffort_NoThrow())
        {
            lock (gate)
            {
                dirty = true;
            }

            flush.Request();
        }
    }

    private bool SaveIndexBestEffort_NoThrow()
    {
        lock (gate)
        {
            index.SetSavedNow(DateTime.UtcNow.Ticks);
            return index.SaveAtomic(fileSystem, metaPath);
        }
    }

    private string GetPayloadPath(string entryId)
        => Path.Combine(payloadDir, entryId + ".bin");
}
