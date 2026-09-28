using System.Security.Cryptography;
using System.Text.Json;

namespace ShaderBuildTool.Spirv;

/// <summary>Persists input-file metadata and content hashes together under the shader output lease.</summary>
internal sealed class ShaderFileHashIndex
{
    private readonly string path;
    private readonly bool verifyContents;
    private readonly Dictionary<string, Entry> previous;
    private readonly Dictionary<string, Entry> visited = new(StringComparer.Ordinal);
    internal int ReusedFiles { get; private set; }
    internal int HashedFiles { get; private set; }

    /// <summary>Records the metadata used by normal builds to recognize unchanged inputs.</summary>
    internal sealed record Entry(long Length, long Modified, long Created, string Hash);
    /// <summary>Versions the single index format independently of compiler result caches.</summary>
    internal sealed record Index(int Version, Dictionary<string, Entry> Files);

    #region Index lifetime
    /// <summary>Loads one index for the entire invocation; malformed metadata simply loses the shortcut.</summary>
    internal ShaderFileHashIndex(string outputRoot, bool verifyContents = false)
    {
        path = Path.Combine(outputRoot, "_cache", "file-hashes.json");
        this.verifyContents = verifyContents;
        previous = new(StringComparer.Ordinal);
        try
        {
            if (!File.Exists(path)) return;
            var index = JsonSerializer.Deserialize(File.ReadAllBytes(path), ShaderFileHashJsonContext.Default.FileIndex);
            if (index is { Version: 1, Files: not null }) previous = index.Files;
        }
        catch (JsonException) { }
        catch (IOException) { }
    }

    /// <summary>Atomically saves only inputs observed this invocation, pruning deleted paths without per-file sidecars.</summary>
    internal void Save()
    {
        var entries = visited.OrderBy(pair => pair.Key, StringComparer.Ordinal)
            .ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);
        byte[] data = JsonSerializer.SerializeToUtf8Bytes(new Index(1, entries), ShaderFileHashJsonContext.Default.FileIndex);
        if (File.Exists(path) && File.ReadAllBytes(path).AsSpan().SequenceEqual(data)) return;
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        string pending = path + ".pending";
        try { File.WriteAllBytes(pending, data); File.Move(pending, path, overwrite: true); }
        finally { File.Delete(pending); }
    }
    #endregion

    #region Input hashing
    /// <summary>Reuses stable metadata matches unless strict verification is requested, otherwise hashes the file stream.</summary>
    internal byte[] GetHash(string file)
    {
        string fullPath = Path.GetFullPath(file);
        var info = new FileInfo(fullPath);
        long length = info.Length, modified = info.LastWriteTimeUtc.Ticks, created = info.CreationTimeUtc.Ticks;
        if (!verifyContents && previous.TryGetValue(fullPath, out var entry) && entry is not null
            && entry.Length == length && entry.Modified == modified && entry.Created == created
            && entry.Hash is { Length: 64 } && entry.Hash.All(Uri.IsHexDigit))
        {
            visited[fullPath] = entry;
            ReusedFiles++;
            return Convert.FromHexString(entry.Hash);
        }

        // Do not persist a hash paired with metadata from a different edit of the input.
        byte[] hash;
        using (var stream = File.OpenRead(fullPath)) hash = SHA256.HashData(stream);
        info.Refresh();
        if (length != info.Length || modified != info.LastWriteTimeUtc.Ticks || created != info.CreationTimeUtc.Ticks)
            throw new IOException("Shader build input changed while hashing: " + fullPath);
        visited[fullPath] = new(length, modified, created, Convert.ToHexString(hash));
        HashedFiles++;
        return hash;
    }
    #endregion
}
