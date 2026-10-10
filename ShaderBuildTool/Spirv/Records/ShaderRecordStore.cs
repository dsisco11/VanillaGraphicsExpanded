using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text.Json;

namespace ShaderBuildTool.Spirv;

/// <summary>Owns atomic, integrity-checked private record files with digest-only references.</summary>
internal sealed class ShaderRecordStore
{
    private readonly string root;
    private readonly ConcurrentDictionary<string, object> locks = new(StringComparer.Ordinal);
    /// <summary>Integrity protects the complete serialized record, including associations and schema.</summary>
    private sealed record Envelope(int Version, string Key, string Digest, byte[] Payload);

    #region Public API
    /// <summary>Scopes records to one fixed cache family; caller-supplied references are never paths.</summary>
    internal ShaderRecordStore(string outputRoot, string family)
    {
        if (family.Length == 0 || family.Any(c => !char.IsAsciiLetter(c))) throw new ArgumentException("Invalid record family.", nameof(family));
        root = Path.Combine(Path.GetFullPath(outputRoot), "_cache", family);
    }

    /// <summary>Recognizes hexadecimal integrity digests separately from Base32 filename keys.</summary>
    internal static bool IsDigest(string? digest) => digest is { Length: 64 } && digest.All(c => c is >= '0' and <= '9' or >= 'A' and <= 'F');

    /// <summary>Computes a stable digest for payload bytes and text artifacts.</summary>
    internal static string Digest(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes));

    /// <summary>Accepts only a complete versioned envelope whose identity and payload remain intact.</summary>
    internal bool TryRead<T>(string key, out T value, Action<string>? miss = null) where T : class
    {
        value = null!;
        if (!ShaderCacheKey.IsValid(key)) { miss?.Invoke("record reference is not a canonical Base32 key"); return false; }
        lock (locks.GetOrAdd(key, static _ => new()))
        {
            try
            {
                var envelope = JsonSerializer.Deserialize<Envelope>(File.ReadAllBytes(Path.Combine(root, key + ".json")));
                if (envelope is not { Version: 1, Payload: not null } || envelope.Key != key || Digest(envelope.Payload) != envelope.Digest)
                    throw new InvalidDataException("record schema, identity, or payload digest invalid");
                value = JsonSerializer.Deserialize<T>(envelope.Payload) ?? throw new InvalidDataException("record payload absent");
                return true;
            }
            catch (Exception ex) when (ex is InvalidDataException or IOException or JsonException or ArgumentException or NotSupportedException or UnauthorizedAccessException)
            { miss?.Invoke("record unavailable: " + ex.Message); return false; }
        }
    }

    /// <summary>Publishes a complete envelope by replacement, synchronizing aliases of the same record.</summary>
    internal void Store<T>(string key, T value) where T : class
    {
        if (!ShaderCacheKey.IsValid(key)) throw new ArgumentException("Record reference must be a canonical Base32 key.", nameof(key));
        byte[] payload = JsonSerializer.SerializeToUtf8Bytes(value);
        byte[] bytes = JsonSerializer.SerializeToUtf8Bytes(new Envelope(1, key, Digest(payload), payload));
        lock (locks.GetOrAdd(key, static _ => new()))
        {
            Directory.CreateDirectory(root);
            string path = Path.Combine(root, key + ".json");
            string pending = path + "." + Guid.NewGuid().ToString("N") + ".pending";
            try { File.WriteAllBytes(pending, bytes); File.Move(pending, path, true); }
            finally { File.Delete(pending); }
        }
    }
    #endregion
}
