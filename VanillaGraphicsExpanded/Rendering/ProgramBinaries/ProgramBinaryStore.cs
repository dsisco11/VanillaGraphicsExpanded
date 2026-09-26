using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json;

namespace VanillaGraphicsExpanded.Rendering.ProgramBinaries;

/// <summary>Stores bounded driver executables and an atomically published JSON index; storage failures are cache misses.</summary>
internal sealed class ProgramBinaryStore(string directory, long maxBytes = 256L << 20, int maxEntries = 512)
{
    internal const int Schema = 1;
    internal const int MaximumBinaryBytes = 32 << 20;
    private readonly object gate = new();

    /// <summary>Describes a binary whose filename is derived solely from a validated content key.</summary>
    public sealed record Entry(int Format, int Size, string Digest, DateTime LastUsedUtc);
    /// <summary>Versions the persisted index independently of driver binaries.</summary>
    public sealed record Index(int Version, Dictionary<string, Entry> Entries);

    #region Storage operations
    /// <summary>Reads and verifies one executable, updating eviction recency while holding the cross-process lease.</summary>
    internal bool TryRead(string key, out int format, out byte[] bytes)
    {
        format = 0; bytes = [];
        if (!ValidKey(key)) return false;
        lock (gate)
        {
            try
            {
                using var lease = Acquire();
                var index = ReadIndex();
                if (!index.Entries.TryGetValue(key, out var entry) || entry.Size <= 0 || entry.Size > MaximumBinaryBytes) return false;
                string path = Path.Combine(directory, key + ".bin");
                if (new FileInfo(path).Length != entry.Size) return false;
                byte[] data = File.ReadAllBytes(path);
                if (Convert.ToHexString(SHA256.HashData(data)) != entry.Digest) return false;
                // Index maintenance is optional; a recency-write failure must not discard a valid executable.
                if (DateTime.UtcNow - entry.LastUsedUtc >= TimeSpan.FromMinutes(1))
                {
                    index.Entries[key] = entry with { LastUsedUtc = DateTime.UtcNow };
                    try { PublishIndex(index); } catch (Exception) { }
                }
                format = entry.Format; bytes = data;
                return true;
            }
            catch (Exception) { return false; }
        }
    }

    /// <summary>Publishes the executable before its index record, pruning stale and least-recently-used entries.</summary>
    internal void Write(string key, int format, byte[] bytes)
    {
        if (!ValidKey(key) || bytes.Length == 0 || bytes.Length > MaximumBinaryBytes || bytes.Length > maxBytes || maxEntries <= 0) return;
        lock (gate)
        {
            try
            {
                using var lease = Acquire();
                var index = ReadIndex();
                index.Entries[key] = new(format, bytes.Length, Convert.ToHexString(SHA256.HashData(bytes)), DateTime.UtcNow);
                // Holding the process lease means no other publisher owns an in-progress temporary file.
                foreach (string path in Directory.EnumerateFiles(directory, "*.tmp")) File.Delete(path);
                long total = index.Entries.Values.Sum(value => (long)value.Size);
                foreach (var item in index.Entries.Where(pair => pair.Key != key).OrderBy(pair => pair.Value.LastUsedUtc).ToArray())
                {
                    if (total <= maxBytes && index.Entries.Count <= maxEntries) break;
                    index.Entries.Remove(item.Key); total -= item.Value.Size;
                }
                // Clean orphaned binaries from interrupted publications as well as evicted entries.
                foreach (string path in Directory.EnumerateFiles(directory, "*.bin"))
                    if (!index.Entries.ContainsKey(Path.GetFileNameWithoutExtension(path))) File.Delete(path);
                AtomicWrite(Path.Combine(directory, key + ".bin"), bytes);
                PublishIndex(index);
            }
            catch (Exception) { /* Cache storage is never required for rendering. */ }
        }
    }

    /// <summary>Invalidates a rejected executable without affecting normal linking.</summary>
    internal void Remove(string key)
    {
        if (!ValidKey(key)) return;
        lock (gate)
        {
            try
            {
                using var lease = Acquire();
                var index = ReadIndex();
                index.Entries.Remove(key);
                PublishIndex(index);
                File.Delete(Path.Combine(directory, key + ".bin"));
            }
            catch (Exception) { }
        }
    }
    #endregion

    #region Index publication
    /// <summary>Takes a nonblocking process lease; contention simply skips caching.</summary>
    private FileStream Acquire()
    {
        Directory.CreateDirectory(directory);
        return new FileStream(Path.Combine(directory, "index.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
    }

    /// <summary>Rejects incompatible, oversized or malformed metadata before reading executable payloads.</summary>
    private Index ReadIndex()
    {
        string path = Path.Combine(directory, "index.json");
        try
        {
            if (new FileInfo(path).Length > (1 << 20)) return new(Schema, []);
            var index = JsonSerializer.Deserialize<Index>(File.ReadAllBytes(path));
            if (index is null || index.Version != Schema || index.Entries is null || index.Entries.Count > 4096 ||
                index.Entries.Any(pair => !ValidKey(pair.Key) || pair.Value is null || pair.Value.Size <= 0 || pair.Value.Size > MaximumBinaryBytes))
                return new(Schema, []);
            return index;
        }
        catch (Exception) { return new(Schema, []); }
    }

    /// <summary>Replaces the versioned JSON index without exposing partially written JSON.</summary>
    private void PublishIndex(Index index) => AtomicWrite(Path.Combine(directory, "index.json"), JsonSerializer.SerializeToUtf8Bytes(index));

    /// <summary>Publishes from a unique same-directory temporary file and cleans it on failure.</summary>
    private static void AtomicWrite(string path, byte[] bytes)
    {
        string temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try { File.WriteAllBytes(temporary, bytes); File.Move(temporary, path, true); }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }

    /// <summary>Allows only SHA-256 hex keys, preventing index data from selecting arbitrary filesystem paths.</summary>
    private static bool ValidKey(string key) => key.Length == 64 && key.All(Uri.IsHexDigit);
    #endregion
}
