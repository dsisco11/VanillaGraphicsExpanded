using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;

using VanillaGraphicsExpanded.Cache;

namespace VanillaGraphicsExpanded.PBR.Materials.Cache;

/// <summary>
/// Implements the shared-core <see cref="ICacheStore"/> contract for the material atlas disk cache,
/// while preserving the existing on-disk format (meta.json + &lt;entryId&gt;.dds payloads).
/// </summary>
internal sealed class MaterialAtlasDiskCacheStore : ICacheStore
{
    private const string MetaIndexFileName = "meta.json";

    private readonly string root;
    private readonly IMaterialAtlasFileSystem fileSystem;

    private readonly ReaderWriterLockSlim indexLock = new(LockRecursionPolicy.NoRecursion);
    private MaterialAtlasDiskCacheIndex index;

    public MaterialAtlasDiskCacheStore(string rootDirectory, IMaterialAtlasFileSystem? fileSystem = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rootDirectory);

        this.fileSystem = fileSystem ?? new MaterialAtlasRealFileSystem();
        root = rootDirectory;

        this.fileSystem.CreateDirectory(root);

        index = MaterialAtlasDiskCacheIndex.CreateEmpty(DateTime.UtcNow.Ticks);
        LoadAndValidateIndex();
    }

    public long TotalBytes
    {
        get
        {
            indexLock.EnterReadLock();
            try
            {
                return index.TotalBytes;
            }
            finally
            {
                indexLock.ExitReadLock();
            }
        }
    }

    public int TotalEntries
    {
        get
        {
            indexLock.EnterReadLock();
            try
            {
                return index.Entries.Count;
            }
            finally
            {
                indexLock.ExitReadLock();
            }
        }
    }

    public bool HasEntryId(string entryId)
    {
        if (string.IsNullOrWhiteSpace(entryId))
        {
            return false;
        }

        indexLock.EnterReadLock();
        try
        {
            return index.Entries.ContainsKey(entryId);
        }
        finally
        {
            indexLock.ExitReadLock();
        }
    }

    public bool TryGetIndexEntry(string entryId, out MaterialAtlasDiskCacheIndex.Entry entry)
    {
        entry = default;

        if (string.IsNullOrWhiteSpace(entryId))
        {
            return false;
        }

        indexLock.EnterReadLock();
        try
        {
            return index.Entries.TryGetValue(entryId, out entry);
        }
        finally
        {
            indexLock.ExitReadLock();
        }
    }

    public void RemoveIndexEntryOnly(string entryId)
        => RemoveIndexEntry(entryId, deletePayload: false);

    public IEnumerable<string> EnumerateEntryIds()
    {
        string[] keys;

        indexLock.EnterReadLock();
        try
        {
            keys = index.Entries.Keys.ToArray();
        }
        finally
        {
            indexLock.ExitReadLock();
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

        MaterialAtlasDiskCacheIndex.Entry entry;

        indexLock.EnterReadLock();
        try
        {
            if (!index.Entries.TryGetValue(entryId, out entry))
            {
                return false;
            }
        }
        finally
        {
            indexLock.ExitReadLock();
        }

        string ddsFileName = entry.DdsFileName ?? (entryId + ".dds");
        string ddsPath = Path.Combine(root, ddsFileName);

        if (!fileSystem.FileExists(ddsPath))
        {
            RemoveIndexEntry(entryId, deletePayload: false);
            return false;
        }

        try
        {
            bytes = fileSystem.ReadAllBytes(ddsPath);
        }
        catch
        {
            TryDelete(ddsPath);
            RemoveIndexEntry(entryId, deletePayload: false);
            bytes = Array.Empty<byte>();
            return false;
        }

        TryUpdateIndexAccess(entryId, ddsPath);
        return true;
    }

    public bool TryWriteAtomic(string entryId, ReadOnlySpan<byte> bytes)
    {
        if (string.IsNullOrWhiteSpace(entryId))
        {
            return false;
        }

        try
        {
            fileSystem.CreateDirectory(root);

            if (!TryParseEntryId(entryId, out string kind, out int schemaVersion, out ulong hash64, out int channels))
            {
                return false;
            }

            // Validate the DDS header cheaply so we can keep meta.json consistent.
            int width;
            int height;
            using (var ms = new MemoryStream(bytes.ToArray(), writable: false))
            {
                if (channels == 3)
                {
                    DdsBc7Rgba8Codec.ReadBc7Header(ms, out width, out height);
                }
                else
                {
                    DdsRgba16UnormCodec.ReadRgba16UnormHeader(ms, out width, out height);
                }
            }

            string ddsPath = Path.Combine(root, entryId + ".dds");
            string tmpDds = ddsPath + "." + Guid.NewGuid().ToString("N", CultureInfo.InvariantCulture) + ".tmp";

            bool stored = false;
            long payloadBytes;

            try
            {
                using (Stream s = fileSystem.OpenWrite(tmpDds))
                {
                    s.Write(bytes);
                    s.Flush();
                }

                ReplaceAtomic(tmpDds, ddsPath);

                payloadBytes = TryGetFileLength(ddsPath);

                UpsertIndexEntry(
                    entryId,
                    kind,
                    schemaVersion,
                    hash64,
                    width,
                    height,
                    channels,
                    payloadBytes);

                stored = true;
            }
            finally
            {
                TryDelete(tmpDds);
            }

            return stored;
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

        string? ddsPath = null;

        indexLock.EnterWriteLock();
        try
        {
            if (!index.Entries.TryGetValue(entryId, out MaterialAtlasDiskCacheIndex.Entry existing))
            {
                return false;
            }

            ddsPath = Path.Combine(root, existing.DdsFileName ?? (entryId + ".dds"));

            index.TotalBytes -= existing.SizeBytes;
            index.Entries.Remove(entryId);

            SaveIndexBestEffort();
        }
        finally
        {
            indexLock.ExitWriteLock();
        }

        if (ddsPath is not null)
        {
            TryDelete(ddsPath);
        }

        return true;
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

        fileSystem.CreateDirectory(root);

        indexLock.EnterWriteLock();
        try
        {
            index = MaterialAtlasDiskCacheIndex.CreateEmpty(DateTime.UtcNow.Ticks);
            SaveIndexBestEffort();
        }
        finally
        {
            indexLock.ExitWriteLock();
        }
    }

    public (long EvictedEntries, long EvictedBytes) PruneToMaxBytes(long maxBytes)
    {
        if (maxBytes <= 0)
        {
            return (0, 0);
        }

        var payloadsToDelete = new List<(string Path, long SizeBytes)>();
        long evictedEntryCount = 0;
        long evictedByteCount = 0;

        indexLock.EnterWriteLock();
        try
        {
            if (index.TotalBytes <= maxBytes)
            {
                return (0, 0);
            }

            foreach ((string entryId, MaterialAtlasDiskCacheIndex.Entry entry) in index.Entries.OrderBy(kvp => kvp.Value.LastAccessUtcTicks).ToList())
            {
                if (index.TotalBytes <= maxBytes)
                {
                    break;
                }

                string ddsPath = Path.Combine(root, entry.DdsFileName ?? (entryId + ".dds"));
                payloadsToDelete.Add((ddsPath, entry.SizeBytes));

                index.TotalBytes -= entry.SizeBytes;
                index.Entries.Remove(entryId);

                evictedEntryCount++;
                evictedByteCount += entry.SizeBytes;
            }

            SaveIndexBestEffort();
        }
        finally
        {
            indexLock.ExitWriteLock();
        }

        foreach ((string path, long _) in payloadsToDelete)
        {
            TryDelete(path);
        }

        return (evictedEntryCount, evictedByteCount);
    }

    private void LoadAndValidateIndex()
    {
        string indexPath = Path.Combine(root, MetaIndexFileName);

        if (!MaterialAtlasDiskCacheIndex.TryLoad(fileSystem, indexPath, out MaterialAtlasDiskCacheIndex loaded))
        {
            return;
        }

        bool changed = false;
        long nowTicks = DateTime.UtcNow.Ticks;

        foreach ((string entryId, MaterialAtlasDiskCacheIndex.Entry entry) in loaded.Entries.ToArray())
        {
            string ddsFileName = entry.DdsFileName ?? (entryId + ".dds");
            string ddsPath = Path.Combine(root, ddsFileName);

            if (!fileSystem.FileExists(ddsPath))
            {
                loaded.Entries.Remove(entryId);
                changed = true;
                continue;
            }

            if (string.Equals(entry.Kind, "materialParams", StringComparison.Ordinal))
            {
                if (entry.Channels != 3)
                {
                    loaded.Entries.Remove(entryId);
                    changed = true;
                    continue;
                }
            }
            else if (string.Equals(entry.Kind, "normalDepth", StringComparison.Ordinal))
            {
                if (entry.Channels != 4)
                {
                    loaded.Entries.Remove(entryId);
                    changed = true;
                    continue;
                }
            }
            else
            {
                loaded.Entries.Remove(entryId);
                changed = true;
                continue;
            }

            try
            {
                long payloadBytes = fileSystem.GetFileLength(ddsPath);
                if (payloadBytes != entry.SizeBytes)
                {
                    loaded.Entries[entryId] = entry with { SizeBytes = payloadBytes };
                    changed = true;
                }
            }
            catch
            {
                loaded.Entries.Remove(entryId);
                changed = true;
            }
        }

        long beforeTotal = loaded.TotalBytes;
        loaded.RecomputeTotals();
        if (loaded.TotalBytes != beforeTotal)
        {
            changed = true;
        }

        indexLock.EnterWriteLock();
        try
        {
            index = loaded;
        }
        finally
        {
            indexLock.ExitWriteLock();
        }

        if (changed)
        {
            try
            {
                loaded.SetSavedNow(nowTicks);
                loaded.SaveAtomic(fileSystem, indexPath);
            }
            catch
            {
                // Best-effort.
            }
        }
    }

    private void UpsertIndexEntry(
        string entryId,
        string kind,
        int schemaVersion,
        ulong hash64,
        int width,
        int height,
        int channels,
        long payloadBytes)
    {
        long nowTicks = DateTime.UtcNow.Ticks;

        indexLock.EnterWriteLock();
        try
        {
            long createdTicks = nowTicks;

            if (index.Entries.TryGetValue(entryId, out MaterialAtlasDiskCacheIndex.Entry existing))
            {
                createdTicks = existing.CreatedUtcTicks != 0 ? existing.CreatedUtcTicks : nowTicks;
                index.TotalBytes -= existing.SizeBytes;
            }

            index.Entries[entryId] = new MaterialAtlasDiskCacheIndex.Entry(
                Kind: kind,
                SchemaVersion: schemaVersion,
                Hash64: hash64,
                Width: width,
                Height: height,
                Channels: channels,
                CreatedUtcTicks: createdTicks,
                LastAccessUtcTicks: nowTicks,
                SizeBytes: payloadBytes,
                DdsFileName: null,
                Provenance: null,
                MetadataPresent: null);

            index.TotalBytes += payloadBytes;

            SaveIndexBestEffort();
        }
        finally
        {
            indexLock.ExitWriteLock();
        }
    }

    private void RemoveIndexEntry(string entryId, bool deletePayload)
    {
        string? ddsPath = null;

        indexLock.EnterWriteLock();
        try
        {
            if (!index.Entries.TryGetValue(entryId, out MaterialAtlasDiskCacheIndex.Entry existing))
            {
                return;
            }

            ddsPath = Path.Combine(root, existing.DdsFileName ?? (entryId + ".dds"));

            index.TotalBytes -= existing.SizeBytes;
            index.Entries.Remove(entryId);

            SaveIndexBestEffort();
        }
        finally
        {
            indexLock.ExitWriteLock();
        }

        if (deletePayload && ddsPath is not null)
        {
            TryDelete(ddsPath);
        }
    }

    private void TryUpdateIndexAccess(string entryId, string ddsPath)
    {
        long nowTicks = DateTime.UtcNow.Ticks;
        long payloadBytes = TryGetFileLength(ddsPath);

        indexLock.EnterWriteLock();
        try
        {
            if (!index.Entries.TryGetValue(entryId, out MaterialAtlasDiskCacheIndex.Entry existing))
            {
                return;
            }

            if (payloadBytes != 0 && payloadBytes != existing.SizeBytes)
            {
                index.TotalBytes -= existing.SizeBytes;
                index.TotalBytes += payloadBytes;
                existing = existing with { SizeBytes = payloadBytes };
            }

            index.Entries[entryId] = existing with { LastAccessUtcTicks = nowTicks };
        }
        finally
        {
            indexLock.ExitWriteLock();
        }
    }

    private long TryGetFileLength(string path)
    {
        try
        {
            return fileSystem.GetFileLength(path);
        }
        catch
        {
            return 0;
        }
    }

    private void TryDelete(string path)
    {
        try
        {
            if (fileSystem.FileExists(path)) fileSystem.DeleteFile(path);
        }
        catch
        {
            // Best-effort.
        }
    }

    private void ReplaceAtomic(string tempPath, string targetPath)
    {
        if (fileSystem.FileExists(targetPath))
        {
            fileSystem.ReplaceFile(tempPath, targetPath);
            return;
        }

        fileSystem.MoveFile(tempPath, targetPath, overwrite: false);
    }

    private void SaveIndexBestEffort()
    {
        try
        {
            index.SetSavedNow(DateTime.UtcNow.Ticks);
            index.SaveAtomic(fileSystem, Path.Combine(root, MetaIndexFileName));
        }
        catch
        {
            // Best-effort.
        }
    }

    private static bool TryParseEntryId(string entryId, out string kind, out int schemaVersion, out ulong hash64, out int channels)
    {
        kind = string.Empty;
        schemaVersion = 0;
        hash64 = 0;
        channels = 0;

        if (entryId.EndsWith(".material", StringComparison.Ordinal))
        {
            kind = "materialParams";
            channels = 3;
        }
        else if (entryId.EndsWith(".norm", StringComparison.Ordinal))
        {
            kind = "normalDepth";
            channels = 4;
        }
        else
        {
            return false;
        }

        int dot = entryId.LastIndexOf('.');
        if (dot <= 0)
        {
            return false;
        }

        string stem = entryId[..dot];
        if (!stem.StartsWith("v", StringComparison.Ordinal))
        {
            return false;
        }

        int dash = stem.IndexOf('-', StringComparison.Ordinal);
        if (dash <= 1)
        {
            return false;
        }

        if (!int.TryParse(stem[1..dash], NumberStyles.Integer, CultureInfo.InvariantCulture, out schemaVersion))
        {
            return false;
        }

        if (!ulong.TryParse(stem[(dash + 1)..], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out hash64))
        {
            return false;
        }

        return schemaVersion > 0;
    }
}
