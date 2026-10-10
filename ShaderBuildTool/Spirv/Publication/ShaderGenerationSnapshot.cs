using System.Security.Cryptography;
using VanillaGraphicsExpanded.Rendering.Spirv;

namespace ShaderBuildTool.Spirv;

/// <summary>Records the complete byte identity of one generation, including its manifest.</summary>
internal sealed record ShaderGenerationSnapshot(Dictionary<string, string> FilesByHash)
{
    #region Public API
    /// <summary>Captures the actual file set without following redirected directories.</summary>
    internal static ShaderGenerationSnapshot Capture(string root) => new(Files(root).ToDictionary(
        path => Path.GetRelativePath(root, path), path => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))), StringComparer.Ordinal));

    /// <summary>Checks reference syntax even when a referenced generation no longer exists.</summary>
    internal void Validate(string root)
    {
        if (FilesByHash is null || FilesByHash.Keys.Distinct(StringComparer.OrdinalIgnoreCase).Count() != FilesByHash.Count)
            throw new InvalidDataException("Invalid generation membership.");
        foreach (var pair in FilesByHash)
        {
            ShaderPublicationPaths.FileWithin(root, pair.Key);
            if (!ShaderRecordStore.IsDigest(pair.Value)) throw new InvalidDataException("Invalid generation digest.");
        }
    }

    /// <summary>Requires exact membership and bytes, rather than accepting a matching subset.</summary>
    internal bool Matches(string root)
    {
        Validate(root);
        if (!Directory.Exists(root)) return false;
        var actual = Capture(root);
        return FilesByHash.Count == actual.FilesByHash.Count && FilesByHash.All(pair => actual.FilesByHash.TryGetValue(pair.Key, out string? hash) && hash == pair.Value);
    }

    /// <summary>Verifies manifest membership and every binary association before a generation is trusted.</summary>
    internal static bool IsCoherent(string root)
    {
        try
        {
            string manifestPath = ShaderPublicationPaths.FileWithin(root, ShaderBinaryDigest.FileName);
            if (!File.Exists(manifestPath)) return false;
            var manifest = ShaderBinaryDigest.Parse(File.ReadAllBytes(manifestPath));
            if (manifest is null) return false;
            var expected = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { ShaderBinaryDigest.FileName };
            foreach (var pair in manifest.Binaries)
            {
                string path = ShaderPublicationPaths.FileWithin(root, pair.Key.Replace('/', Path.DirectorySeparatorChar));
                if (!expected.Add(Path.GetRelativePath(root, path)) || !File.Exists(path)) return false;
                byte[] bytes = File.ReadAllBytes(path);
                if (!ShaderBinaryDigest.TryRead(manifest, pair.Key, bytes.Length, out var digest) || !SHA256.HashData(bytes).AsSpan().SequenceEqual(digest)) return false;
            }
            return expected.SetEquals(Files(root).Select(path => Path.GetRelativePath(root, path)));
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or ArgumentException) { return false; }
    }

    /// <summary>Enumerates owned files, rejecting reparse points before descending into directories.</summary>
    internal static IEnumerable<string> Files(string root)
    {
        ShaderPublicationPaths.EnsureUnredirected(root);
        foreach (string file in Directory.EnumerateFiles(root))
        {
            ShaderPublicationPaths.EnsureUnredirected(file);
            yield return file;
        }
        foreach (string directory in Directory.EnumerateDirectories(root))
        {
            ShaderPublicationPaths.EnsureUnredirected(directory);
            foreach (string file in Files(directory)) yield return file;
        }
    }
    #endregion
}
