using System;
using System.Buffers;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Numerics.Tensors;
using System.Threading;

using Vintagestory.API.Config;

namespace VanillaGraphicsExpanded.PBR.Materials.Cache;

internal sealed class MaterialAtlasDiskCache : IMaterialAtlasDiskCache
{
    private const int MetaFormatVersion = 1;

    private static readonly byte[] MetaMagic = "VGEDC1\0\0"u8.ToArray();

    private readonly string root;
    private readonly long maxBytes;

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

    public MaterialAtlasDiskCache(string rootDirectory, long maxBytes)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rootDirectory);
        if (maxBytes <= 0) throw new ArgumentOutOfRangeException(nameof(maxBytes));

        root = rootDirectory;
        this.maxBytes = maxBytes;

        Directory.CreateDirectory(root);
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
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
        catch
        {
            // Best-effort.
        }

        Directory.CreateDirectory(root);
    }

    public MaterialAtlasDiskCacheStats GetStatsSnapshot()
    {
        long totalEntries = 0;
        long totalBytes = 0;

        try
        {
            foreach (CacheEntry entry in EnumerateEntries())
            {
                totalEntries++;
                totalBytes += entry.SizeBytes;
            }
        }
        catch
        {
            // Best-effort.
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
        if (!TryLoadTile(PayloadKind.MaterialParams, key, out TileMeta meta, out float[] rgba))
        {
            rgbTriplets = Array.Empty<float>();
            Interlocked.Increment(ref materialParamsMisses);
            return false;
        }

        int pixels = checked(meta.Width * meta.Height);
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

    public bool TryLoadNormalDepthTile(AtlasCacheKey key, out float[] rgbaQuads)
    {
        if (!TryLoadTile(PayloadKind.NormalDepth, key, out TileMeta meta, out float[] rgba))
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

    private bool TryLoadTile(PayloadKind kind, AtlasCacheKey key, out TileMeta meta, out float[] rgba)
    {
        meta = default;
        rgba = Array.Empty<float>();

        try
        {
            string stem = GetFileStem(key);

            string metaPath = GetMetaPath(root, stem, kind);
            string ddsPath = GetDdsPath(root, stem, kind);

            bool hasMeta = File.Exists(metaPath);
            bool hasDds = File.Exists(ddsPath);

            if (!hasMeta || !hasDds)
            {
                // Clean up orphans from interrupted writes.
                if (hasMeta && !hasDds)
                {
                    TryDelete(metaPath);
                }
                else if (!hasMeta && hasDds)
                {
                    TryDelete(ddsPath);
                }

                return false;
            }

            if (!TryReadMeta(metaPath, out meta))
            {
                TryDeletePair(metaPath, ddsPath);
                return false;
            }

            if (meta.Kind != kind || meta.SchemaVersion != key.SchemaVersion || meta.Hash64 != key.Hash64)
            {
                TryDeletePair(metaPath, ddsPath);
                return false;
            }

            try
            {
                if (kind == PayloadKind.MaterialParams)
                {
                    byte[] rgbaU8 = DdsBc7Rgba8Codec.ReadRgba8FromDds(ddsPath, out int w, out int h);
                    if (w != meta.Width || h != meta.Height)
                    {
                        TryDeletePair(metaPath, ddsPath);
                        rgba = Array.Empty<float>();
                        return false;
                    }

                    rgba = new float[rgbaU8.Length];
                    for (int i = 0; i < rgbaU8.Length; i++)
                    {
                        rgba[i] = rgbaU8[i] * InvU8Max;
                    }
                }
                else
                {
                    ushort[] rgbaU16 = DdsRgba16UnormCodec.ReadRgba16Unorm(ddsPath, out int w, out int h);
                    if (w != meta.Width || h != meta.Height)
                    {
                        TryDeletePair(metaPath, ddsPath);
                        rgba = Array.Empty<float>();
                        return false;
                    }

                    rgba = new float[rgbaU16.Length];
                    for (int i = 0; i < rgbaU16.Length; i++)
                    {
                        rgba[i] = rgbaU16[i] * InvU16Max;
                    }
                }
            }
            catch
            {
                TryDeletePair(metaPath, ddsPath);
                rgba = Array.Empty<float>();
                return false;
            }

            // Touch meta timestamp for LRU.
            TouchMeta(metaPath);

            return true;
        }
        catch
        {
            // If anything looks wrong, treat as a cache miss.
            meta = default;
            rgba = Array.Empty<float>();
            return false;
        }
    }

    private void StoreTile(PayloadKind kind, AtlasCacheKey key, int width, int height, int channels, ushort[] rgbaU16)
    {
        try
        {
            Directory.CreateDirectory(root);

            string stem = GetFileStem(key);

            string metaPath = GetMetaPath(root, stem, kind);
            string ddsPath = GetDdsPath(root, stem, kind);

            var meta = new TileMeta(
                Kind: kind,
                SchemaVersion: key.SchemaVersion,
                Hash64: key.Hash64,
                Width: width,
                Height: height,
                Channels: channels,
                CreatedUtcTicks: DateTime.UtcNow.Ticks);

            byte[] metaBytes = WriteMeta(meta);

            string tmpMeta = metaPath + "." + Guid.NewGuid().ToString("N", CultureInfo.InvariantCulture) + ".tmp";
            string tmpDds = ddsPath + "." + Guid.NewGuid().ToString("N", CultureInfo.InvariantCulture) + ".tmp";

            bool stored = false;

            try
            {
                // Write payload first, then publish metadata last.
                DdsRgba16UnormCodec.WriteRgba16Unorm(tmpDds, width, height, rgbaU16);
                ReplaceAtomic(tmpDds, ddsPath);

                WriteAllBytesAtomic(tmpMeta, metaBytes, metaPath);
                TouchMeta(metaPath);

                stored = true;
            }
            finally
            {
                TryDelete(tmpMeta);
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
            Directory.CreateDirectory(root);

            string stem = GetFileStem(key);

            string metaPath = GetMetaPath(root, stem, kind);
            string ddsPath = GetDdsPath(root, stem, kind);

            var meta = new TileMeta(
                Kind: kind,
                SchemaVersion: key.SchemaVersion,
                Hash64: key.Hash64,
                Width: width,
                Height: height,
                Channels: channels,
                CreatedUtcTicks: DateTime.UtcNow.Ticks);

            byte[] metaBytes = WriteMeta(meta);

            string tmpMeta = metaPath + "." + Guid.NewGuid().ToString("N", CultureInfo.InvariantCulture) + ".tmp";
            string tmpDds = ddsPath + "." + Guid.NewGuid().ToString("N", CultureInfo.InvariantCulture) + ".tmp";

            bool stored = false;

            try
            {
                DdsBc7Rgba8Codec.WriteBc7Dds(tmpDds, width, height, rgbaU8);
                ReplaceAtomic(tmpDds, ddsPath);

                WriteAllBytesAtomic(tmpMeta, metaBytes, metaPath);
                TouchMeta(metaPath);

                stored = true;
            }
            finally
            {
                TryDelete(tmpMeta);
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
        // Avoid scanning every tile write.
        DateTime now = DateTime.UtcNow;
        if ((now - lastPruneUtc).TotalSeconds < 10)
        {
            return;
        }

        lastPruneUtc = now;

        try
        {
            long totalBytes = GetTotalCacheBytes();
            if (totalBytes <= maxBytes)
            {
                return;
            }

            // Oldest meta timestamp == least recently used.
            List<CacheEntry> entries = EnumerateEntries().OrderBy(e => e.LastAccessUtc).ToList();

            foreach (CacheEntry entry in entries)
            {
                if (totalBytes <= maxBytes)
                {
                    break;
                }

                long freed = entry.SizeBytes;
                TryDeletePair(entry.MetaPath, entry.DdsPath);
                totalBytes -= freed;

                Interlocked.Increment(ref evictedEntries);
                Interlocked.Add(ref evictedBytes, freed);
            }
        }
        catch
        {
            // Best-effort.
        }
    }

    private long GetTotalCacheBytes()
    {
        long total = 0;
        foreach (CacheEntry entry in EnumerateEntries())
        {
            total += entry.SizeBytes;
        }

        return total;
    }

    private IEnumerable<CacheEntry> EnumerateEntries()
    {
        foreach (CacheEntry entry in EnumerateEntriesInDir(root, useSuffixedNames: true))
        {
            yield return entry;
        }
    }

    private IEnumerable<CacheEntry> EnumerateEntriesInDir(string dir, bool useSuffixedNames)
    {
        if (!Directory.Exists(dir))
        {
            yield break;
        }

        foreach (string metaPath in Directory.EnumerateFiles(dir, "*.meta", SearchOption.TopDirectoryOnly))
        {
            string file = Path.GetFileName(metaPath);

            PayloadKind kind;
            string stem;

            if (useSuffixedNames)
            {
                if (file.EndsWith(".material.meta", StringComparison.OrdinalIgnoreCase))
                {
                    kind = PayloadKind.MaterialParams;
                    stem = file[..^(".material.meta".Length)];
                }
                else if (file.EndsWith(".norm.meta", StringComparison.OrdinalIgnoreCase))
                {
                    kind = PayloadKind.NormalDepth;
                    stem = file[..^(".norm.meta".Length)];
                }
                else
                {
                    continue;
                }
            }
            else
            {
                kind = dir.EndsWith("normaldepth", StringComparison.OrdinalIgnoreCase)
                    ? PayloadKind.NormalDepth
                    : PayloadKind.MaterialParams;
                stem = Path.GetFileNameWithoutExtension(metaPath);
            }

            string ddsPath = useSuffixedNames
                ? GetDdsPath(dir, stem, kind)
                : Path.Combine(dir, stem + ".dds");

            if (!File.Exists(ddsPath))
            {
                continue;
            }

            DateTime lastAccess = File.GetLastWriteTimeUtc(metaPath);
            long size;

            try
            {
                size = new FileInfo(metaPath).Length + new FileInfo(ddsPath).Length;
            }
            catch
            {
                continue;
            }

            yield return new CacheEntry(kind, metaPath, ddsPath, lastAccess, size);
        }
    }

    private static string GetKindSuffix(PayloadKind kind)
        => kind switch
        {
            PayloadKind.MaterialParams => ".material",
            PayloadKind.NormalDepth => ".norm",
            _ => ".unknown",
        };

    private static string GetMetaPath(string dir, string stem, PayloadKind kind)
        => Path.Combine(dir, stem + GetKindSuffix(kind) + ".meta");

    private static string GetDdsPath(string dir, string stem, PayloadKind kind)
        => Path.Combine(dir, stem + GetKindSuffix(kind) + ".dds");

    private static string GetFileStem(AtlasCacheKey key)
        // Windows-safe (':' is invalid in filenames).
        => string.Format(CultureInfo.InvariantCulture, "v{0}-{1:x16}", key.SchemaVersion, key.Hash64);

    private static void TouchMeta(string metaPath)
    {
        try
        {
            File.SetLastWriteTimeUtc(metaPath, DateTime.UtcNow);
        }
        catch
        {
            // Best-effort.
        }
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch
        {
            // Best-effort.
        }
    }

    private static void TryDeletePair(string metaPath, string ddsPath)
    {
        TryDelete(metaPath);
        TryDelete(ddsPath);
    }

    private static void ReplaceAtomic(string tempPath, string targetPath)
    {
        if (File.Exists(targetPath))
        {
            File.Replace(tempPath, targetPath, destinationBackupFileName: null, ignoreMetadataErrors: true);
            return;
        }

        File.Move(tempPath, targetPath);
    }

    private static void WriteAllBytesAtomic(string tempPath, byte[] data, string targetPath)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(targetPath)!);

        using (var fs = new FileStream(tempPath, FileMode.Create, FileAccess.Write, FileShare.None))
        {
            fs.Write(data, 0, data.Length);
            fs.Flush(flushToDisk: true);
        }

        ReplaceAtomic(tempPath, targetPath);
    }

    private static bool TryReadMeta(string metaPath, out TileMeta meta)
    {
        meta = default;

        byte[] bytes;
        try
        {
            bytes = File.ReadAllBytes(metaPath);
        }
        catch
        {
            return false;
        }

        if (bytes.Length < 8 + 4 + 1 + 4 + 8 + 4 + 4 + 1 + 8)
        {
            return false;
        }

        try
        {
            int o = 0;

            // magic
            for (int i = 0; i < MetaMagic.Length; i++)
            {
                if (bytes[o + i] != MetaMagic[i])
                {
                    return false;
                }
            }

            o += MetaMagic.Length;

            int version = BitConverter.ToInt32(bytes, o);
            o += 4;

            if (version != MetaFormatVersion)
            {
                return false;
            }

            PayloadKind kind = (PayloadKind)bytes[o++];
            int schemaVersion = BitConverter.ToInt32(bytes, o);
            o += 4;

            ulong hash64 = BitConverter.ToUInt64(bytes, o);
            o += 8;

            int width = BitConverter.ToInt32(bytes, o);
            o += 4;

            int height = BitConverter.ToInt32(bytes, o);
            o += 4;

            int channels = bytes[o++];

            long createdTicks = BitConverter.ToInt64(bytes, o);

            if (width <= 0 || height <= 0)
            {
                return false;
            }

            meta = new TileMeta(kind, schemaVersion, hash64, width, height, channels, createdTicks);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static byte[] WriteMeta(TileMeta meta)
    {
        byte[] bytes = new byte[checked(MetaMagic.Length + 4 + 1 + 4 + 8 + 4 + 4 + 1 + 8)];
        int o = 0;

        Array.Copy(MetaMagic, 0, bytes, 0, MetaMagic.Length);
        o += MetaMagic.Length;

        BitConverter.GetBytes(MetaFormatVersion).CopyTo(bytes, o);
        o += 4;

        bytes[o++] = (byte)meta.Kind;

        BitConverter.GetBytes(meta.SchemaVersion).CopyTo(bytes, o);
        o += 4;

        BitConverter.GetBytes(meta.Hash64).CopyTo(bytes, o);
        o += 8;

        BitConverter.GetBytes(meta.Width).CopyTo(bytes, o);
        o += 4;

        BitConverter.GetBytes(meta.Height).CopyTo(bytes, o);
        o += 4;

        bytes[o++] = (byte)meta.Channels;

        BitConverter.GetBytes(meta.CreatedUtcTicks).CopyTo(bytes, o);

        return bytes;
    }

    private readonly record struct TileMeta(
        PayloadKind Kind,
        int SchemaVersion,
        ulong Hash64,
        int Width,
        int Height,
        int Channels,
        long CreatedUtcTicks);

    private readonly record struct CacheEntry(
        PayloadKind Kind,
        string MetaPath,
        string DdsPath,
        DateTime LastAccessUtc,
        long SizeBytes);
}
