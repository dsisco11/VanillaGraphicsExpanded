using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace VanillaGraphicsExpanded.Cache.Disk;

internal sealed class DiskCacheIndex
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

    public Dictionary<string, Entry> Entries { get; } = new(StringComparer.Ordinal);

    private DiskCacheIndex()
    {
    }

    public static DiskCacheIndex CreateEmpty(long nowUtcTicks)
        => new()
        {
            SchemaVersion = CurrentSchemaVersion,
            CreatedUtcTicks = nowUtcTicks,
            LastSavedUtcTicks = nowUtcTicks,
        };

    public static bool TryLoad(IDiskCacheFileSystem fileSystem, string filePath, out DiskCacheIndex index)
    {
        ArgumentNullException.ThrowIfNull(fileSystem);
        index = CreateEmpty(DateTime.UtcNow.Ticks);

        try
        {
            if (!fileSystem.FileExists(filePath))
            {
                return false;
            }

            byte[] bytes = fileSystem.ReadAllBytes(filePath);
            MetaJsonDto? dto = JsonSerializer.Deserialize<MetaJsonDto>(bytes, JsonOptions);
            if (dto is null || dto.SchemaVersion != CurrentSchemaVersion || dto.Entries is null)
            {
                return false;
            }

            var loaded = new DiskCacheIndex
            {
                SchemaVersion = dto.SchemaVersion,
                CreatedUtcTicks = dto.CreatedUtcTicks,
                LastSavedUtcTicks = dto.LastSavedUtcTicks,
            };

            foreach ((string key, EntryDto edto) in dto.Entries)
            {
                if (string.IsNullOrWhiteSpace(key) || !TryMapEntry(edto, out Entry entry))
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
            index = CreateEmpty(DateTime.UtcNow.Ticks);
            return false;
        }
    }

    public void SetSavedNow(long nowUtcTicks)
    {
        if (CreatedUtcTicks == 0)
        {
            CreatedUtcTicks = nowUtcTicks;
        }

        LastSavedUtcTicks = nowUtcTicks;
    }

    public bool SaveAtomic(IDiskCacheFileSystem fileSystem, string filePath)
    {
        ArgumentNullException.ThrowIfNull(fileSystem);

        try
        {
            var dto = new MetaJsonDto
            {
                SchemaVersion = SchemaVersion,
                CreatedUtcTicks = CreatedUtcTicks,
                LastSavedUtcTicks = LastSavedUtcTicks,
                Entries = new Dictionary<string, EntryDto>(Entries.Count, StringComparer.Ordinal),
            };

            foreach ((string key, Entry entry) in Entries)
            {
                if (string.IsNullOrWhiteSpace(key))
                {
                    continue;
                }

                dto.Entries[key] = new EntryDto
                {
                    SizeBytes = entry.SizeBytes,
                    CreatedUtcTicks = entry.CreatedUtcTicks,
                    LastAccessUtcTicks = entry.LastAccessUtcTicks,
                };
            }

            byte[] json = JsonSerializer.SerializeToUtf8Bytes(dto, JsonOptions);
            return AtomicDiskFile.TryWriteAtomic(fileSystem, filePath, json);
        }
        catch
        {
            return false;
        }
    }

    private static bool TryMapEntry(EntryDto dto, out Entry entry)
    {
        entry = default;

        if (dto.SizeBytes < 0 || dto.CreatedUtcTicks <= 0 || dto.LastAccessUtcTicks <= 0)
        {
            return false;
        }

        entry = new Entry(
            SizeBytes: dto.SizeBytes,
            CreatedUtcTicks: dto.CreatedUtcTicks,
            LastAccessUtcTicks: dto.LastAccessUtcTicks);
        return true;
    }

    internal readonly record struct Entry(long SizeBytes, long CreatedUtcTicks, long LastAccessUtcTicks);

    private sealed class MetaJsonDto
    {
        [JsonPropertyName("schemaVersion")]
        public int SchemaVersion { get; set; }

        [JsonPropertyName("createdUtcTicks")]
        public long CreatedUtcTicks { get; set; }

        [JsonPropertyName("lastSavedUtcTicks")]
        public long LastSavedUtcTicks { get; set; }

        [JsonPropertyName("entries")]
        public Dictionary<string, EntryDto>? Entries { get; set; }
    }

    private sealed class EntryDto
    {
        [JsonPropertyName("sizeBytes")]
        public long SizeBytes { get; set; }

        [JsonPropertyName("createdUtcTicks")]
        public long CreatedUtcTicks { get; set; }

        [JsonPropertyName("lastAccessUtcTicks")]
        public long LastAccessUtcTicks { get; set; }
    }
}
