using System;
using System.Buffers;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Numerics.Tensors;
using System.Threading;

using VanillaGraphicsExpanded.Profiling;

using Vintagestory.API.Config;

namespace VanillaGraphicsExpanded.PBR.Materials.Cache;

internal sealed class MaterialAtlasDiskCache : IMaterialAtlasDiskCache
{
    private const string MetaIndexFileName = "meta.json";

    private readonly string root;
    private readonly long maxBytes;
    private readonly IMaterialAtlasFileSystem fileSystem;

    private readonly ReaderWriterLockSlim indexLock = new(LockRecursionPolicy.NoRecursion);
    private MaterialAtlasDiskCacheIndex index;

    private DateTime lastPruneUtc = DateTime.MinValue;

    private long materialParamsHits;
    private long materialParamsMisses;
    private long materialParamsStores;

    private long normalDepthHits;
    private long normalDepthMisses;
    private long normalDepthStores;

    private long evictedEntries;
    private long evictedBytes;

    private const float InvU8Max = 1f / 255f;
    private const float InvU16Max = 1f / 65535f;

    private enum PayloadKind : byte
    {
        MaterialParams = 1,
        NormalDepth = 2,
    }

    public MaterialAtlasDiskCache(string rootDirectory, long maxBytes, IMaterialAtlasFileSystem? fileSystem = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rootDirectory);
        if (maxBytes <= 0) throw new ArgumentOutOfRangeException(nameof(maxBytes));

        this.fileSystem = fileSystem ?? new MaterialAtlasRealFileSystem();

        root = rootDirectory;
        this.maxBytes = maxBytes;

        this.fileSystem.CreateDirectory(root);

        index = MaterialAtlasDiskCacheIndex.CreateEmpty(DateTime.UtcNow.Ticks);
        LoadAndValidateIndex();
    }

    public bool HasMaterialParamsTile(AtlasCacheKey key)
        => HasTile(PayloadKind.MaterialParams, key);

    private void LoadAndValidateIndex()
    {
        string indexPath = Path.Combine(root, MetaIndexFileName);

        if (!MaterialAtlasDiskCacheIndex.TryLoad(fileSystem, indexPath, out MaterialAtlasDiskCacheIndex loaded))
        {
            return;
        }

        bool changed = false;
        long nowTicks = DateTime.UtcNow.Ticks;

        // Validate entries against payload existence only.
        // Corruption/dimension checks are deferred to actual reads to avoid opening every cached payload at startup.
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

            // Basic sanity: unknown kinds or channel counts are dropped early.
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
                // Best-effort: do not fail cache init.
            }
        }
    }

    public static MaterialAtlasDiskCache CreateDefault()
    {
        // Matches docs/MaterialSystem.Cache.Architecture.md: VintagestoryData/VGE/Cache/
        string root = Path.Combine(GamePaths.DataPath, "VGE", "Cache");

        // Conservative default until a config knob is added.
        const long DefaultMaxBytes = 512L * 1024 * 1024;

        return new MaterialAtlasDiskCache(root, DefaultMaxBytes);
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
        finally
        {
            indexLock.ExitWriteLock();
        }
    }

    public MaterialAtlasDiskCacheStats GetStatsSnapshot()
    {
        long totalEntries;
        long totalBytes;

        indexLock.EnterReadLock();
        try
        {
            totalEntries = index.Entries.Count;
            totalBytes = index.TotalBytes;
        }
        finally
        {
            indexLock.ExitReadLock();
        }

        return new MaterialAtlasDiskCacheStats(
            MaterialParams: new MaterialAtlasDiskCacheStats.Payload(
                Hits: Interlocked.Read(ref materialParamsHits),
                Misses: Interlocked.Read(ref materialParamsMisses),
                Stores: Interlocked.Read(ref materialParamsStores)),
            NormalDepth: new MaterialAtlasDiskCacheStats.Payload(
                Hits: Interlocked.Read(ref normalDepthHits),
                Misses: Interlocked.Read(ref normalDepthMisses),
                Stores: Interlocked.Read(ref normalDepthStores)),
            TotalEntries: totalEntries,
            TotalBytes: totalBytes,
            EvictedEntries: Interlocked.Read(ref evictedEntries),
            EvictedBytes: Interlocked.Read(ref evictedBytes));
    }

    public bool TryLoadMaterialParamsTile(AtlasCacheKey key, out float[] rgbTriplets)
    {
        if (!TryLoadTile(PayloadKind.MaterialParams, key, out TileInfo info, out float[] rgba))
        {
            rgbTriplets = Array.Empty<float>();
            Interlocked.Increment(ref materialParamsMisses);
            return false;
        }

        int pixels = checked(info.Width * info.Height);
        int expectedRgba = checked(pixels * 4);

        if (rgba.Length != expectedRgba)
        {
            rgbTriplets = Array.Empty<float>();
            Interlocked.Increment(ref materialParamsMisses);
            return false;
        }

        // Stored as BC7-compressed DDS (8-bit RGBA); strip A when serving RGB callers.
        rgbTriplets = new float[checked(pixels * 3)];

        int si = 0;
        int di = 0;
        for (int p = 0; p < pixels; p++)
        {
            rgbTriplets[di++] = rgba[si++];
            rgbTriplets[di++] = rgba[si++];
            rgbTriplets[di++] = rgba[si++];
            si++; // skip A
        }

        Interlocked.Increment(ref materialParamsHits);

        return true;
    }

    public void StoreMaterialParamsTile(AtlasCacheKey key, int width, int height, float[] rgbTriplets)
    {
        ArgumentNullException.ThrowIfNull(rgbTriplets);

        int pixels = checked(width * height);
        if (rgbTriplets.Length != checked(pixels * 3))
        {
            return;
        }

        byte[] rgbU8 = new byte[rgbTriplets.Length];
        QuantizeUnorm8(rgbTriplets, rgbU8);

        var rgbaU8 = new byte[checked(pixels * 4)];

        int si = 0;
        int di = 0;
        for (int p = 0; p < pixels; p++)
        {
            rgbaU8[di++] = rgbU8[si++];
            rgbaU8[di++] = rgbU8[si++];
            rgbaU8[di++] = rgbU8[si++];
            rgbaU8[di++] = 255;
        }

        StoreTileBc7(PayloadKind.MaterialParams, key, width, height, channels: 3, rgbaU8);
    }

    public bool HasNormalDepthTile(AtlasCacheKey key)
        => HasTile(PayloadKind.NormalDepth, key);

    public bool TryLoadNormalDepthTile(AtlasCacheKey key, out float[] rgbaQuads)
    {
        if (!TryLoadTile(PayloadKind.NormalDepth, key, out TileInfo info, out float[] rgba))
        {
            rgbaQuads = Array.Empty<float>();
            Interlocked.Increment(ref normalDepthMisses);
            return false;
        }

        rgbaQuads = rgba;
        Interlocked.Increment(ref normalDepthHits);
        return true;
    }

    public void StoreNormalDepthTile(AtlasCacheKey key, int width, int height, float[] rgbaQuads)
    {
        ArgumentNullException.ThrowIfNull(rgbaQuads);

        int pixels = checked(width * height);
        if (rgbaQuads.Length != checked(pixels * 4))
        {
            return;
        }

        var rgbaU16 = new ushort[rgbaQuads.Length];
        QuantizeUnorm16(rgbaQuads, rgbaU16);

        StoreTile(PayloadKind.NormalDepth, key, width, height, channels: 4, rgbaU16);
    }

    private bool HasTile(PayloadKind kind, AtlasCacheKey key)
    {
        if (key.SchemaVersion == 0)
        {
            return false;
        }

        string entryId;
        MaterialAtlasDiskCacheIndex.Entry entry;

        try
        {
            string stem = GetFileStem(key);
            entryId = stem + GetKindSuffix(kind);

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
        }
        catch
        {
            return false;
        }

        string expectedKind = GetIndexKind(kind);
        return string.Equals(entry.Kind, expectedKind, StringComparison.Ordinal)
            && entry.SchemaVersion == key.SchemaVersion
            && entry.Hash64 == key.Hash64;
    }

    private readonly record struct TileInfo(int Width, int Height, int Channels);

    private bool TryLoadTile(PayloadKind kind, AtlasCacheKey key, out TileInfo info, out float[] rgba)
    {
        info = default;
        rgba = Array.Empty<float>();

        using var cacheReadScope = Profiler.BeginScope(
            kind == PayloadKind.MaterialParams
                ? "MaterialAtlasDiskCache.Read.MaterialParams"
                : "MaterialAtlasDiskCache.Read.NormalDepth",
            category: "Cache");

        string stem;
        string entryId;
        MaterialAtlasDiskCacheIndex.Entry entry;

        try
        {
            stem = GetFileStem(key);
            entryId = stem + GetKindSuffix(kind);

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
        }
        catch
        {
            return false;
        }

        string expectedKind = GetIndexKind(kind);
        if (!string.Equals(entry.Kind, expectedKind, StringComparison.Ordinal)
            || entry.SchemaVersion != key.SchemaVersion
            || entry.Hash64 != key.Hash64)
        {
            // Stale/mismatched index record.
            RemoveIndexEntry(entryId, deletePayload: false);
            return false;
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
            if (kind == PayloadKind.MaterialParams)
            {
                using Stream s = fileSystem.OpenRead(ddsPath);
                byte[] rgbaU8 = DdsBc7Rgba8Codec.ReadRgba8FromDds(s, out int w, out int h);
                if (w != entry.Width || h != entry.Height)
                {
                    throw new InvalidDataException("Cached DDS dimensions did not match index entry.");
                }

                rgba = new float[rgbaU8.Length];
                for (int i = 0; i < rgbaU8.Length; i++)
                {
                    rgba[i] = rgbaU8[i] * InvU8Max;
                }

                info = new TileInfo(entry.Width, entry.Height, Channels: 3);
            }
            else
            {
                using Stream s = fileSystem.OpenRead(ddsPath);
                ushort[] rgbaU16 = DdsRgba16UnormCodec.ReadRgba16Unorm(s, out int w, out int h);
                if (w != entry.Width || h != entry.Height)
                {
                    throw new InvalidDataException("Cached DDS dimensions did not match index entry.");
                }

                rgba = new float[rgbaU16.Length];
                for (int i = 0; i < rgbaU16.Length; i++)
                {
                    rgba[i] = rgbaU16[i] * InvU16Max;
                }

                info = new TileInfo(entry.Width, entry.Height, Channels: 4);
            }
        }
        catch
        {
            // Corrupt/unreadable payload; treat as a cache miss and evict the bad entry.
            TryDelete(ddsPath);
            RemoveIndexEntry(entryId, deletePayload: false);
            rgba = Array.Empty<float>();
            info = default;
            return false;
        }

        TryUpdateIndexAccess(entryId, ddsPath);
        return true;
    }

    private void StoreTile(PayloadKind kind, AtlasCacheKey key, int width, int height, int channels, ushort[] rgbaU16)
    {
        try
        {
            fileSystem.CreateDirectory(root);

            string stem = GetFileStem(key);
            string entryId = stem + GetKindSuffix(kind);
            string ddsPath = Path.Combine(root, entryId + ".dds");

            string tmpDds = ddsPath + "." + Guid.NewGuid().ToString("N", CultureInfo.InvariantCulture) + ".tmp";

            bool stored = false;
            long payloadBytes = 0;

            try
            {
                using (Stream s = fileSystem.OpenWrite(tmpDds))
                {
                    DdsRgba16UnormCodec.WriteRgba16Unorm(s, width, height, rgbaU16);
                    s.Flush();
                }

                ReplaceAtomic(tmpDds, ddsPath);

                payloadBytes = TryGetFileLength(ddsPath);
                UpsertIndexEntry(kind, key, entryId, width, height, channels, payloadBytes);

                stored = true;
            }
            finally
            {
                TryDelete(tmpDds);
            }

            if (stored)
            {
                if (kind == PayloadKind.MaterialParams)
                {
                    Interlocked.Increment(ref materialParamsStores);
                }
                else if (kind == PayloadKind.NormalDepth)
                {
                    Interlocked.Increment(ref normalDepthStores);
                }
            }

            TryPruneIfNeeded();
        }
        catch
        {
            // Best-effort: failures should not crash the build pipeline.
        }
    }

    private void StoreTileBc7(PayloadKind kind, AtlasCacheKey key, int width, int height, int channels, byte[] rgbaU8)
    {
        try
        {
            fileSystem.CreateDirectory(root);

            string stem = GetFileStem(key);
            string entryId = stem + GetKindSuffix(kind);
            string ddsPath = Path.Combine(root, entryId + ".dds");

            string tmpDds = ddsPath + "." + Guid.NewGuid().ToString("N", CultureInfo.InvariantCulture) + ".tmp";

            bool stored = false;
            long payloadBytes = 0;

            try
            {
                using (Stream s = fileSystem.OpenWrite(tmpDds))
                {
                    DdsBc7Rgba8Codec.WriteBc7Dds(s, width, height, rgbaU8);
                    s.Flush();
                }

                ReplaceAtomic(tmpDds, ddsPath);

                payloadBytes = TryGetFileLength(ddsPath);
                UpsertIndexEntry(kind, key, entryId, width, height, channels, payloadBytes);

                stored = true;
            }
            finally
            {
                TryDelete(tmpDds);
            }

            if (stored)
            {
                if (kind == PayloadKind.MaterialParams)
                {
                    Interlocked.Increment(ref materialParamsStores);
                }
                else if (kind == PayloadKind.NormalDepth)
                {
                    Interlocked.Increment(ref normalDepthStores);
                }
            }

            TryPruneIfNeeded();
        }
        catch
        {
            // Best-effort.
        }
    }

    private static string GetIndexKind(PayloadKind kind)
        => kind switch
        {
            PayloadKind.MaterialParams => "materialParams",
            PayloadKind.NormalDepth => "normalDepth",
            _ => "unknown",
        };

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

    private void UpsertIndexEntry(PayloadKind kind, AtlasCacheKey key, string entryId, int width, int height, int channels, long payloadBytes)
    {
        long nowTicks = DateTime.UtcNow.Ticks;
        string indexPath = Path.Combine(root, MetaIndexFileName);

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
                Kind: GetIndexKind(kind),
                SchemaVersion: key.SchemaVersion,
                Hash64: key.Hash64,
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

            try
            {
                index.SetSavedNow(nowTicks);
                index.SaveAtomic(fileSystem, indexPath);
            }
            catch
            {
                // Best-effort.
            }
        }
        finally
        {
            indexLock.ExitWriteLock();
        }
    }

    private void RemoveIndexEntry(string entryId, bool deletePayload)
    {
        string? ddsPath = null;
        long nowTicks = DateTime.UtcNow.Ticks;
        string indexPath = Path.Combine(root, MetaIndexFileName);

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

            try
            {
                index.SetSavedNow(nowTicks);
                index.SaveAtomic(fileSystem, indexPath);
            }
            catch
            {
                // Best-effort.
            }
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

    private static void QuantizeUnorm8(ReadOnlySpan<float> src, Span<byte> dst)
    {
        if (dst.Length != src.Length)
        {
            throw new ArgumentException("Destination length must match source length.", nameof(dst));
        }

        for (int i = 0; i < src.Length; i++)
        {
            float v = src[i];
            if (v < 0f) v = 0f;
            if (v > 1f) v = 1f;
            v = (v * 255f) + 0.5f;
            if (v < 0f) v = 0f;
            if (v > 255f) v = 255f;
            dst[i] = (byte)v;
        }
    }

    private static void QuantizeUnorm16(ReadOnlySpan<float> src, Span<ushort> dst)
    {
        if (dst.Length != src.Length)
        {
            throw new ArgumentException("Destination length must match source length.", nameof(dst));
        }

        // Quantization:
        // u16 = trunc(clamp(clamp(x,0,1) * 65535 + 0.5, 0, 65535))
        // Uses TensorPrimitives for vectorized clamp/mul/add + conversion.
        float[] tmpArray = ArrayPool<float>.Shared.Rent(src.Length);
        try
        {
            Span<float> tmp = tmpArray.AsSpan(0, src.Length);
            src.CopyTo(tmp);

            TensorPrimitives.Clamp(tmp, 0f, 1f, tmp);
            TensorPrimitives.Multiply(tmp, 65535f, tmp);
            TensorPrimitives.Add(tmp, 0.5f, tmp);
            TensorPrimitives.Clamp(tmp, 0f, 65535f, tmp);

            TensorPrimitives.ConvertTruncating<float, ushort>(tmp, dst);
        }
        finally
        {
            ArrayPool<float>.Shared.Return(tmpArray, clearArray: false);
        }
    }

    private void TryPruneIfNeeded()
    {
        // Avoid pruning on every tile write.
        DateTime now = DateTime.UtcNow;
        if ((now - lastPruneUtc).TotalSeconds < 10)
        {
            return;
        }

        lastPruneUtc = now;

        var payloadsToDelete = new List<(string Path, long SizeBytes)>();
        long evictedEntryCount = 0;
        long evictedByteCount = 0;

        indexLock.EnterWriteLock();
        try
        {
            if (index.TotalBytes <= maxBytes)
            {
                return;
            }

            // Oldest access tick == least recently used.
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
        finally
        {
            indexLock.ExitWriteLock();
        }

        foreach ((string path, long _) in payloadsToDelete)
        {
            TryDelete(path);
        }

        if (evictedEntryCount != 0)
        {
            Interlocked.Add(ref evictedEntries, evictedEntryCount);
            Interlocked.Add(ref evictedBytes, evictedByteCount);
        }
    }

    private static string GetKindSuffix(PayloadKind kind)
        => kind switch
        {
            PayloadKind.MaterialParams => ".material",
            PayloadKind.NormalDepth => ".norm",
            _ => ".unknown",
        };

    private static string GetFileStem(AtlasCacheKey key)
        // Windows-safe (':' is invalid in filenames).
        => string.Format(CultureInfo.InvariantCulture, "v{0}-{1:x16}", key.SchemaVersion, key.Hash64);

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
}
