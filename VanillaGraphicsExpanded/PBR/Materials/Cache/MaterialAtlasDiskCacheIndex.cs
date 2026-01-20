using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace VanillaGraphicsExpanded.PBR.Materials.Cache;

internal sealed class MaterialAtlasDiskCacheIndex
{
    public const int CurrentSchemaVersion = 1;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        NumberHandling = JsonNumberHandling.Strict,
    };

    public int SchemaVersion { get; private set; }

    public long CreatedUtcTicks { get; private set; }

    public long LastSavedUtcTicks { get; private set; }

    public long TotalBytes { get; private set; }

    public Dictionary<string, Entry> Entries { get; } = new(StringComparer.Ordinal);

    private MaterialAtlasDiskCacheIndex()
    {
    }

    public static MaterialAtlasDiskCacheIndex CreateEmpty(long nowUtcTicks)
        => new()
        {
            SchemaVersion = CurrentSchemaVersion,
            CreatedUtcTicks = nowUtcTicks,
            LastSavedUtcTicks = nowUtcTicks,
            TotalBytes = 0,
        };

    public static bool TryLoad(string filePath, out MaterialAtlasDiskCacheIndex index)
    {
        index = CreateEmpty(DateTime.UtcNow.Ticks);

        try
        {
            if (!File.Exists(filePath))
            {
                return false;
            }

            byte[] bytes = File.ReadAllBytes(filePath);
            MetaJsonDto? dto = JsonSerializer.Deserialize<MetaJsonDto>(bytes, JsonOptions);
            if (dto is null || dto.SchemaVersion != CurrentSchemaVersion || dto.Entries is null)
            {
                return false;
            }

            var loaded = new MaterialAtlasDiskCacheIndex
            {
                SchemaVersion = dto.SchemaVersion,
                CreatedUtcTicks = dto.CreatedUtcTicks,
                LastSavedUtcTicks = dto.LastSavedUtcTicks,
                TotalBytes = dto.TotalBytes,
            };

            foreach ((string key, EntryDto edto) in dto.Entries)
            {
                if (string.IsNullOrWhiteSpace(key))
                {
                    continue;
                }

                if (!TryMapEntry(edto, out Entry entry))
                {
                    continue;
                }

                loaded.Entries[key] = entry;
            }

            index = loaded;
            return true;
        }
        catch
        {
            // Treat as missing/invalid.
            index = CreateEmpty(DateTime.UtcNow.Ticks);
            return false;
        }
    }

    public void RecomputeTotals()
    {
        long total = 0;
        foreach (Entry entry in Entries.Values)
        {
            total += entry.SizeBytes;
        }

        TotalBytes = total;
    }

    public void SetSavedNow(long nowUtcTicks)
    {
        if (CreatedUtcTicks == 0)
        {
            CreatedUtcTicks = nowUtcTicks;
        }

        LastSavedUtcTicks = nowUtcTicks;
    }

    public void SaveAtomic(string filePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);

        Directory.CreateDirectory(Path.GetDirectoryName(filePath)!);

        var dto = new MetaJsonDto
        {
            SchemaVersion = SchemaVersion,
            CreatedUtcTicks = CreatedUtcTicks,
            LastSavedUtcTicks = LastSavedUtcTicks,
            TotalBytes = TotalBytes,
            Entries = new Dictionary<string, EntryDto>(Entries.Count, StringComparer.Ordinal),
        };

        foreach ((string key, Entry entry) in Entries)
        {
            dto.Entries[key] = new EntryDto
            {
                Kind = entry.Kind,
                SchemaVersion = entry.SchemaVersion,
                Hash64 = entry.Hash64.ToString("x16", CultureInfo.InvariantCulture),
                Width = entry.Width,
                Height = entry.Height,
                Channels = entry.Channels,
                CreatedUtcTicks = entry.CreatedUtcTicks,
                LastAccessUtcTicks = entry.LastAccessUtcTicks,
                SizeBytes = entry.SizeBytes,
                DdsFileName = entry.DdsFileName,
                Provenance = entry.Provenance,
                MetadataPresent = entry.MetadataPresent,
            };
        }

        byte[] bytes = JsonSerializer.SerializeToUtf8Bytes(dto, JsonOptions);

        string tmpPath = filePath + "." + Guid.NewGuid().ToString("N", CultureInfo.InvariantCulture) + ".tmp";
        try
        {
            using (var fs = new FileStream(tmpPath, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                fs.Write(bytes, 0, bytes.Length);
                fs.Flush(flushToDisk: true);
            }

            if (File.Exists(filePath))
            {
                File.Replace(tmpPath, filePath, destinationBackupFileName: null, ignoreMetadataErrors: true);
            }
            else
            {
                File.Move(tmpPath, filePath);
            }
        }
        finally
        {
            try
            {
                if (File.Exists(tmpPath))
                {
                    File.Delete(tmpPath);
                }
            }
            catch
            {
                // Best-effort.
            }
        }
    }

    private static bool TryMapEntry(EntryDto dto, out Entry entry)
    {
        entry = default;

        if (dto.SchemaVersion <= 0)
        {
            return false;
        }

        if (dto.Width <= 0 || dto.Height <= 0)
        {
            return false;
        }

        if (dto.Channels <= 0)
        {
            return false;
        }

        if (string.IsNullOrWhiteSpace(dto.Kind))
        {
            return false;
        }

        if (string.IsNullOrWhiteSpace(dto.Hash64) || !ulong.TryParse(dto.Hash64, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out ulong hash64))
        {
            return false;
        }

        entry = new Entry(
            Kind: dto.Kind,
            SchemaVersion: dto.SchemaVersion,
            Hash64: hash64,
            Width: dto.Width,
            Height: dto.Height,
            Channels: dto.Channels,
            CreatedUtcTicks: dto.CreatedUtcTicks,
            LastAccessUtcTicks: dto.LastAccessUtcTicks,
            SizeBytes: dto.SizeBytes,
            DdsFileName: dto.DdsFileName,
            Provenance: dto.Provenance,
            MetadataPresent: dto.MetadataPresent);

        return true;
    }

    public readonly record struct Entry(
        string Kind,
        int SchemaVersion,
        ulong Hash64,
        int Width,
        int Height,
        int Channels,
        long CreatedUtcTicks,
        long LastAccessUtcTicks,
        long SizeBytes,
        string? DdsFileName,
        string? Provenance,
        Dictionary<string, bool>? MetadataPresent);

    private sealed class MetaJsonDto
    {
        [JsonPropertyName("schemaVersion")]
        public int SchemaVersion { get; set; }

        [JsonPropertyName("createdUtcTicks")]
        public long CreatedUtcTicks { get; set; }

        [JsonPropertyName("lastSavedUtcTicks")]
        public long LastSavedUtcTicks { get; set; }

        [JsonPropertyName("totalBytes")]
        public long TotalBytes { get; set; }

        [JsonPropertyName("entries")]
        public Dictionary<string, EntryDto>? Entries { get; set; }
    }

    private sealed class EntryDto
    {
        [JsonPropertyName("kind")]
        public string? Kind { get; set; }

        [JsonPropertyName("schemaVersion")]
        public int SchemaVersion { get; set; }

        [JsonPropertyName("hash64")]
        public string? Hash64 { get; set; }

        [JsonPropertyName("width")]
        public int Width { get; set; }

        [JsonPropertyName("height")]
        public int Height { get; set; }

        [JsonPropertyName("channels")]
        public int Channels { get; set; }

        [JsonPropertyName("createdUtcTicks")]
        public long CreatedUtcTicks { get; set; }

        [JsonPropertyName("lastAccessUtcTicks")]
        public long LastAccessUtcTicks { get; set; }

        [JsonPropertyName("sizeBytes")]
        public long SizeBytes { get; set; }

        [JsonPropertyName("ddsFileName")]
        public string? DdsFileName { get; set; }

        [JsonPropertyName("provenance")]
        public string? Provenance { get; set; }

        [JsonPropertyName("metadataPresent")]
        public Dictionary<string, bool>? MetadataPresent { get; set; }
    }
}
