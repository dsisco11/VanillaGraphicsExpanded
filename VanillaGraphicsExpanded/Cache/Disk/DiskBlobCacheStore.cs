using System;
using System.Collections.Generic;
using System.IO;

namespace VanillaGraphicsExpanded.Cache.Disk;

public sealed class DiskBlobCacheStore : ICacheStore
{
    private const string MetaFileName = "meta.json";

    private readonly IDiskCacheFileSystem fileSystem;
    private readonly string root;
    private readonly string payloadDir;
    private readonly string metaPath;

    private DiskCacheIndex index;

    public DiskBlobCacheStore(string rootDirectory)
        : this(rootDirectory, fileSystem: null)
    {
    }

    internal DiskBlobCacheStore(string rootDirectory, IDiskCacheFileSystem? fileSystem)
    {
        if (string.IsNullOrWhiteSpace(rootDirectory)) throw new ArgumentException("Root directory must be provided", nameof(rootDirectory));

        this.fileSystem = fileSystem ?? new RealDiskCacheFileSystem();
        root = rootDirectory;
        payloadDir = Path.Combine(root, "payloads");
        metaPath = Path.Combine(root, MetaFileName);

        EnsureDirs();
        index = LoadOrCreateIndex();
    }

    public IEnumerable<string> EnumerateEntryIds()
    {
        foreach (string key in index.Entries.Keys)
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

        if (!index.Entries.ContainsKey(entryId))
        {
            return false;
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

            return SaveIndexBestEffort();
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

        if (index.Entries.Remove(entryId))
        {
            changed = true;
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
            SaveIndexBestEffort();
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
        index = DiskCacheIndex.CreateEmpty(DateTime.UtcNow.Ticks);
        SaveIndexBestEffort();
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
        empty.SaveAtomic(fileSystem, metaPath);
        return empty;
    }

    private void Touch(string entryId, long sizeBytes)
    {
        long now = DateTime.UtcNow.Ticks;
        if (index.Entries.TryGetValue(entryId, out DiskCacheIndex.Entry entry))
        {
            index.Entries[entryId] = entry with
            {
                SizeBytes = sizeBytes,
                LastAccessUtcTicks = now,
            };
        }

        SaveIndexBestEffort();
    }

    private bool SaveIndexBestEffort()
    {
        index.SetSavedNow(DateTime.UtcNow.Ticks);
        return index.SaveAtomic(fileSystem, metaPath);
    }

    private string GetPayloadPath(string entryId)
        => Path.Combine(payloadDir, entryId + ".bin");
}
