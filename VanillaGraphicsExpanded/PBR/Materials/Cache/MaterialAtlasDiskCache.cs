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
using Vintagestory.API.Common;

namespace VanillaGraphicsExpanded.PBR.Materials.Cache;

internal sealed class MaterialAtlasDiskCache : IMaterialAtlasDiskCache
{
    private readonly string root;
    private readonly long maxBytes;
    private readonly MaterialAtlasDiskCacheStore store;

    private ILogger? logger;
    private int loggedWriteFailures;

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

        root = rootDirectory;
        this.maxBytes = maxBytes;
        store = new MaterialAtlasDiskCacheStore(rootDirectory, fileSystem);
    }

    public string RootDirectory => root;

    public void SetLogger(ILogger? logger)
    {
        this.logger = logger;
    }

    public bool HasMaterialParamsTile(AtlasCacheKey key)
        => HasTile(PayloadKind.MaterialParams, key);

    public int CountExisting(MaterialAtlasDiskCachePayloadKind kind, IReadOnlyList<AtlasCacheKey> keys)
    {
        ArgumentNullException.ThrowIfNull(keys);

        if (keys.Count == 0)
        {
            return 0;
        }

        PayloadKind payloadKind = kind switch
        {
            MaterialAtlasDiskCachePayloadKind.MaterialParams => PayloadKind.MaterialParams,
            MaterialAtlasDiskCachePayloadKind.NormalDepth => PayloadKind.NormalDepth,
            _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, message: "Unknown payload kind."),
        };

        int hits = 0;
        for (int i = 0; i < keys.Count; i++)
        {
            if (HasTile(payloadKind, keys[i]))
            {
                hits++;
            }
        }

        return hits;
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
        store.Clear();
    }

    public MaterialAtlasDiskCacheStats GetStatsSnapshot()
    {
        long totalEntries = store.TotalEntries;
        long totalBytes = store.TotalBytes;

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

        try
        {
            string entryId = GetFileStem(key) + GetKindSuffix(kind);

            if (!store.TryGetIndexEntry(entryId, out MaterialAtlasDiskCacheIndex.Entry entry))
            {
                return false;
            }

            string expectedKind = GetIndexKind(kind);
            return string.Equals(entry.Kind, expectedKind, StringComparison.Ordinal)
                && entry.SchemaVersion == key.SchemaVersion
                && entry.Hash64 == key.Hash64;
        }
        catch
        {
            return false;
        }
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

        string entryId;
        byte[] ddsBytes;
        MaterialAtlasDiskCacheIndex.Entry entry;

        try
        {
            entryId = GetFileStem(key) + GetKindSuffix(kind);
        }
        catch
        {
            return false;
        }

        if (!store.TryGetIndexEntry(entryId, out entry))
        {
            return false;
        }

        string expectedKind = GetIndexKind(kind);
        if (!string.Equals(entry.Kind, expectedKind, StringComparison.Ordinal)
            || entry.SchemaVersion != key.SchemaVersion
            || entry.Hash64 != key.Hash64)
        {
            // Stale/mismatched index record.
            store.RemoveIndexEntryOnly(entryId);
            return false;
        }

        if (!store.TryRead(entryId, out ddsBytes))
        {
            return false;
        }

        try
        {
            if (kind == PayloadKind.MaterialParams)
            {
                using Stream s = new MemoryStream(ddsBytes, writable: false);
                byte[] rgbaU8 = DdsBc7Rgba8Codec.ReadRgba8FromDds(s, out int w, out int h);

                rgba = new float[rgbaU8.Length];
                for (int i = 0; i < rgbaU8.Length; i++)
                {
                    rgba[i] = rgbaU8[i] * InvU8Max;
                }

                info = new TileInfo(w, h, Channels: 3);
            }
            else
            {
                using Stream s = new MemoryStream(ddsBytes, writable: false);
                ushort[] rgbaU16 = DdsRgba16UnormCodec.ReadRgba16Unorm(s, out int w, out int h);

                rgba = new float[rgbaU16.Length];
                for (int i = 0; i < rgbaU16.Length; i++)
                {
                    rgba[i] = rgbaU16[i] * InvU16Max;
                }

                info = new TileInfo(w, h, Channels: 4);
            }
        }
        catch
        {
            // Corrupt/unreadable payload; treat as a cache miss and evict the bad entry.
            store.TryRemove(entryId);
            rgba = Array.Empty<float>();
            info = default;
            return false;
        }
        return true;
    }

    private static string GetIndexKind(PayloadKind kind)
        => kind switch
        {
            PayloadKind.MaterialParams => "materialParams",
            PayloadKind.NormalDepth => "normalDepth",
            _ => "unknown",
        };

    private void StoreTile(PayloadKind kind, AtlasCacheKey key, int width, int height, int channels, ushort[] rgbaU16)
    {
        try
        {
            string entryId = GetFileStem(key) + GetKindSuffix(kind);

            using var ms = new MemoryStream(capacity: 1024);
            DdsRgba16UnormCodec.WriteRgba16Unorm(ms, width, height, rgbaU16);
            byte[] ddsBytes = ms.ToArray();

            if (store.TryWriteAtomic(entryId, ddsBytes))
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
            else
            {
                // Rate-limit to avoid log spam if disk writes are failing repeatedly.
                if (logger is not null && Interlocked.Increment(ref loggedWriteFailures) <= 3)
                {
                    logger.Warning(
                        "[VGE] Material atlas disk cache write failed: entryId={0}, root={1}, reason={2}",
                        entryId,
                        root,
                        store.LastWriteFailure ?? "(unknown)");
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
            string entryId = GetFileStem(key) + GetKindSuffix(kind);

            using var ms = new MemoryStream(capacity: 1024);
            DdsBc7Rgba8Codec.WriteBc7Dds(ms, width, height, rgbaU8);
            byte[] ddsBytes = ms.ToArray();

            if (store.TryWriteAtomic(entryId, ddsBytes))
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
            else
            {
                if (logger is not null && Interlocked.Increment(ref loggedWriteFailures) <= 3)
                {
                    logger.Warning(
                        "[VGE] Material atlas disk cache write failed: entryId={0}, root={1}, reason={2}",
                        entryId,
                        root,
                        store.LastWriteFailure ?? "(unknown)");
                }
            }

            TryPruneIfNeeded();
        }
        catch
        {
            // Best-effort.
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

        (long evictedEntryCount, long evictedByteCount) = store.PruneToMaxBytes(maxBytes);
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
}
