using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace VanillaGraphicsExpanded.Cache.Disk;

public sealed class DiskJsonDictionaryCacheStore : ICacheStore, IDisposable
{
    private const int CurrentSchemaVersion = 1;

    private static readonly TimeSpan DefaultDebounceDelay = TimeSpan.FromMilliseconds(250);

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        NumberHandling = JsonNumberHandling.Strict,
    };

    private readonly IDiskCacheFileSystem fileSystem;
    private readonly string root;
    private readonly string path;

    private readonly object gate = new();
    private readonly DebouncedFlushScheduler flush;
    private bool dirty;

    private Meta meta;

    public DiskJsonDictionaryCacheStore(string rootDirectory, string fileName = "meta.json")
        : this(rootDirectory, fileName, fileSystem: null)
    {
    }

    internal DiskJsonDictionaryCacheStore(string rootDirectory, string fileName, IDiskCacheFileSystem? fileSystem, TimeSpan? debounceDelay = null)
    {
        if (string.IsNullOrWhiteSpace(rootDirectory)) throw new ArgumentException("Root directory must be provided", nameof(rootDirectory));
        if (string.IsNullOrWhiteSpace(fileName)) throw new ArgumentException("File name must be provided", nameof(fileName));

        this.fileSystem = fileSystem ?? new RealDiskCacheFileSystem();
        root = rootDirectory;
        path = Path.Combine(root, fileName);

        flush = new DebouncedFlushScheduler(FlushMetaBestEffort, debounceDelay ?? DefaultDebounceDelay);

        EnsureDirs();
        meta = LoadOrCreate();
    }

    public void Dispose()
    {
        try
        {
            // Best-effort: persist any pending updates.
            FlushMetaBestEffort();
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
            keys = meta.Entries.Keys.ToArray();
        }

        foreach (string key in keys)
        {
            yield return key;
        }
    }

    public bool Contains(string entryId)
    {
        if (string.IsNullOrWhiteSpace(entryId))
        {
            return false;
        }

        lock (gate)
        {
            return meta.Entries.ContainsKey(entryId);
        }
    }

    public bool TryRead(string entryId, out byte[] bytes)
    {
        bytes = Array.Empty<byte>();

        if (string.IsNullOrWhiteSpace(entryId))
        {
            return false;
        }

        Entry? e;
        lock (gate)
        {
            if (!meta.Entries.TryGetValue(entryId, out e) || e is null)
            {
                return false;
            }
        }

        if (e is null)
        {
            return false;
        }

        try
        {
            bytes = JsonSerializer.SerializeToUtf8Bytes(e.Value, JsonOptions);
            Touch(entryId);
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
            using JsonDocument doc = JsonDocument.Parse(bytes.ToArray());
            JsonElement value = doc.RootElement.Clone();

            long now = DateTime.UtcNow.Ticks;
            lock (gate)
            {
                if (!meta.Entries.ContainsKey(entryId))
                {
                    meta.Entries[entryId] = new Entry
                    {
                        CreatedUtcTicks = now,
                        LastAccessUtcTicks = now,
                        Value = value,
                    };
                }
                else
                {
                    Entry existing = meta.Entries[entryId]!;
                    existing.LastAccessUtcTicks = now;
                    existing.Value = value;
                }

                meta.LastSavedUtcTicks = now;
                dirty = true;
            }

            // Debounced: reduce meta.json rewrites when lots of entries are produced.
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

        lock (gate)
        {
            if (!meta.Entries.Remove(entryId))
            {
                return false;
            }

            meta.LastSavedUtcTicks = DateTime.UtcNow.Ticks;
            dirty = true;
        }

        flush.Request();
        return true;
    }

    public void Clear()
    {
        lock (gate)
        {
            meta = new Meta
            {
                SchemaVersion = CurrentSchemaVersion,
                CreatedUtcTicks = DateTime.UtcNow.Ticks,
                LastSavedUtcTicks = DateTime.UtcNow.Ticks,
                Entries = new Dictionary<string, Entry>(StringComparer.Ordinal),
            };

            dirty = true;
        }

        flush.Request();
    }

    private void Touch(string entryId)
    {
        lock (gate)
        {
            if (!meta.Entries.TryGetValue(entryId, out Entry? existing) || existing is null)
            {
                return;
            }

            existing.LastAccessUtcTicks = DateTime.UtcNow.Ticks;
            meta.LastSavedUtcTicks = DateTime.UtcNow.Ticks;
            dirty = true;
        }

        flush.Request();
    }

    private void FlushMetaBestEffort()
    {
        Meta snapshot;

        lock (gate)
        {
            if (!dirty)
            {
                return;
            }

            snapshot = CloneMeta(meta);
            dirty = false;
        }

        if (!SaveAtomic(snapshot))
        {
            lock (gate)
            {
                dirty = true;
            }

            flush.Request();
        }
    }

    private bool SaveAtomic()
    {
        Meta snapshot;
        lock (gate)
        {
            snapshot = CloneMeta(meta);
        }

        return SaveAtomic(snapshot);
    }

    private bool SaveAtomic(Meta snapshot)
    {
        try
        {
            EnsureDirs();

            byte[] json = JsonSerializer.SerializeToUtf8Bytes(snapshot, JsonOptions);
            return AtomicDiskFile.TryWriteAtomic(fileSystem, path, json);
        }
        catch
        {
            return false;
        }
    }

    private static Meta CloneMeta(Meta source)
    {
        var clone = new Meta
        {
            SchemaVersion = source.SchemaVersion,
            CreatedUtcTicks = source.CreatedUtcTicks,
            LastSavedUtcTicks = source.LastSavedUtcTicks,
            Entries = new Dictionary<string, Entry>(source.Entries.Count, StringComparer.Ordinal),
        };

        foreach ((string key, Entry value) in source.Entries)
        {
            clone.Entries[key] = new Entry
            {
                CreatedUtcTicks = value.CreatedUtcTicks,
                LastAccessUtcTicks = value.LastAccessUtcTicks,
                Value = value.Value,
            };
        }

        return clone;
    }

    private void EnsureDirs()
    {
        if (!fileSystem.DirectoryExists(root))
        {
            fileSystem.CreateDirectory(root);
        }
    }

    private Meta LoadOrCreate()
    {
        try
        {
            if (fileSystem.FileExists(path))
            {
                byte[] bytes = fileSystem.ReadAllBytes(path);
                Meta? loaded = JsonSerializer.Deserialize<Meta>(bytes, JsonOptions);
                if (loaded is not null && loaded.SchemaVersion == CurrentSchemaVersion && loaded.Entries is not null)
                {
                    return new Meta
                    {
                        SchemaVersion = loaded.SchemaVersion,
                        CreatedUtcTicks = loaded.CreatedUtcTicks,
                        LastSavedUtcTicks = loaded.LastSavedUtcTicks,
                        Entries = new Dictionary<string, Entry>(loaded.Entries, StringComparer.Ordinal),
                    };
                }
            }
        }
        catch
        {
            // Treat as missing/corrupt.
        }

        var empty = new Meta
        {
            SchemaVersion = CurrentSchemaVersion,
            CreatedUtcTicks = DateTime.UtcNow.Ticks,
            LastSavedUtcTicks = DateTime.UtcNow.Ticks,
            Entries = new Dictionary<string, Entry>(StringComparer.Ordinal),
        };

        meta = empty;
        SaveAtomic();
        return empty;
    }

    private sealed class Meta
    {
        public int SchemaVersion { get; set; }

        public long CreatedUtcTicks { get; set; }

        public long LastSavedUtcTicks { get; set; }

        public Dictionary<string, Entry> Entries { get; set; } = new(StringComparer.Ordinal);
    }

    private sealed class Entry
    {
        public long CreatedUtcTicks { get; set; }

        public long LastAccessUtcTicks { get; set; }

        public JsonElement Value { get; set; }
    }
}
