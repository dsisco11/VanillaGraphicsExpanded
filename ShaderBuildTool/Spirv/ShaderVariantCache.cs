using System.Security.Cryptography;
using System.Text.Json;
using VanillaGraphicsExpanded.Rendering.Spirv;

namespace ShaderBuildTool.Spirv;

/// <summary>Stores verified compiler results independently of the currently published shader catalog.</summary>
internal sealed class ShaderVariantCache(string outputRoot, string compilerIdentity)
{
    internal string CompilerIdentity { get; } = compilerIdentity;
    private readonly string root = Path.Combine(outputRoot, "_cache");
    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, object> entryLocks = new(StringComparer.Ordinal);

    #region Public API
    /// <summary>Includes final source, layout, defines, entry point and compiler policy in an unambiguous content key.</summary>
    internal string Key(string source, string stage, string entryPoint, string? inputPath = null, string? workingDirectory = null) => ShaderCacheKey.Create("variant-cache-v3", CompilerIdentity, stage, entryPoint, source,
            ShaderCompilerProcess.GenerateDebugInfo && inputPath != null ? Path.GetFullPath(inputPath) : "",
            ShaderCompilerProcess.GenerateDebugInfo && workingDirectory != null ? Path.GetFullPath(workingDirectory) : "");

    /// <summary>Accepts only complete entries whose bytes still match the recorded digest.</summary>
    internal bool TryRead(string key, out byte[] bytes, out ShaderBinaryDigest.Entry digest, Action<string>? reportMiss = null)
    {
        if (!ShaderCacheKey.IsValid(key)) { bytes = []; digest = null!; reportMiss?.Invoke("invalid compiler cache reference"); return false; }
        // The output lease excludes other builders, but aliases within this batch can share a key.
        // Keep native file reads out of the atomic replacement window on platforms denying delete sharing.
        lock (entryLocks.GetOrAdd(key, static _ => new object()))
        {
            bytes = [];
            digest = null!;
            try
            {
                string metadata = Path.Combine(root, key + ".json");
                string binary = Path.Combine(root, key + ".bin");
                if (!File.Exists(metadata)) { reportMiss?.Invoke("cache key has no metadata"); return false; }
                if (!File.Exists(binary)) { reportMiss?.Invoke("cached binary missing"); return false; }
                var expected = JsonSerializer.Deserialize<ShaderBinaryDigest.Entry>(File.ReadAllBytes(metadata));
                bytes = File.ReadAllBytes(binary);
                var actual = Digest(bytes);
                if (expected != actual || bytes.Length == 0) { reportMiss?.Invoke("cached binary digest/length invalid"); return false; }
                digest = actual;
                return true;
            }
            catch (JsonException) { reportMiss?.Invoke("cache metadata malformed"); return false; }
            catch (IOException) { reportMiss?.Invoke("cache entry unreadable"); return false; }
        }
    }

    /// <summary>Publishes metadata last so interrupted writes cannot validate a partial compiler result.</summary>
    internal void Store(string key, byte[] bytes, ShaderBinaryDigest.Entry digest)
    {
        if (!ShaderCacheKey.IsValid(key) || bytes.Length == 0 || Digest(bytes) != digest)
            throw new InvalidDataException("Compiler cache key or binary digest is invalid.");
        // Serialize only this entry; different compiler results still publish concurrently.
        lock (entryLocks.GetOrAdd(key, static _ => new object()))
        {
            Directory.CreateDirectory(root);
            WriteAtomic(Path.Combine(root, key + ".bin"), bytes);
            WriteAtomic(Path.Combine(root, key + ".json"), JsonSerializer.SerializeToUtf8Bytes(digest));
        }
    }

    /// <summary>Restores a cached output only when its bytes differ, preserving unaffected output timestamps.</summary>
    internal static void Publish(string path, byte[] bytes)
    {
        if (File.Exists(path) && File.ReadAllBytes(path).AsSpan().SequenceEqual(bytes)) return;
        WriteAtomic(path, bytes);
    }

    /// <summary>Computes the runtime digest from successful compiler output.</summary>
    internal static ShaderBinaryDigest.Entry Digest(byte[] bytes) => new(bytes.Length, Convert.ToHexString(SHA256.HashData(bytes)));

    #endregion

    #region Private
    /// <summary>Replaces a file only after all bytes have been written under the output lease.</summary>
    private static void WriteAtomic(string path, byte[] bytes)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        // Distinct catalog paths can share identical compiler input and therefore the same cache key.
        string pending = path + "." + Guid.NewGuid().ToString("N") + ".pending";
        try { File.WriteAllBytes(pending, bytes); File.Move(pending, path, overwrite: true); }
        finally { File.Delete(pending); }
    }
    #endregion
}
