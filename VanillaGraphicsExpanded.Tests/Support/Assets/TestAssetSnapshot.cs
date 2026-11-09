using System.Collections.Frozen;
using System.Collections.Concurrent;
using System.Text;

namespace VanillaGraphicsExpanded.Tests.Support.Assets;

/// <summary>Loads immutable build inputs once per test process, independently of fixture overrides and logical asset reads.</summary>
internal sealed class TestAssetSnapshot
{
    private readonly FrozenDictionary<string, byte[]> files;
    private readonly Lazy<FrozenDictionary<string, byte[]>> assets;
    private readonly ConcurrentDictionary<string, string> text = new(StringComparer.Ordinal);
    private static readonly ConcurrentDictionary<string, Lazy<TestAssetSnapshot>> diagnosticDirectories = new(StringComparer.OrdinalIgnoreCase);
    private static readonly ConcurrentDictionary<string, Lazy<byte[]>> diagnosticFiles = new(StringComparer.OrdinalIgnoreCase);
    internal static TestAssetSnapshot Built { get; } = LoadBuilt();

    #region Internal API
    /// <summary>Freezes authored or deliberately malformed inputs independently of their caller's mutable arrays.</summary>
    internal TestAssetSnapshot(IReadOnlyDictionary<string, byte[]> inputs)
    {
        files = inputs.ToFrozenDictionary(pair => Normalize(pair.Key), pair => pair.Value.ToArray(), StringComparer.Ordinal);
        assets = new(() => files.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase));
    }

    /// <summary>Captures one explicitly selected build-output directory before consumers run against memory.</summary>
    private TestAssetSnapshot(string root) : this(root, Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories)) { }

    /// <summary>Freezes an explicitly requested diagnostic capture once; consumers read only the immutable provider.</summary>
    internal static TestAssetSnapshot CaptureDirectory(string root) => diagnosticDirectories.GetOrAdd(Path.GetFullPath(root),
        path => new Lazy<TestAssetSnapshot>(() => new(path))).Value;

    /// <summary>Freezes an explicitly requested diagnostic binary once and gives each consumer isolated bytes.</summary>
    internal static byte[] CaptureFile(string path) => diagnosticFiles.GetOrAdd(Path.GetFullPath(path),
        name => new Lazy<byte[]>(() => File.ReadAllBytes(name))).Value.ToArray();

    /// <summary>Captures only the explicitly supplied current input set.</summary>
    private TestAssetSnapshot(string root, IEnumerable<string> inputs)
    {
        // This is the sole physical load of immutable inputs. Mutable/reload scenarios own their
        // overrides; callers never receive the cached byte arrays themselves.
        files = inputs.Distinct(OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal).ToFrozenDictionary(
            path => Path.GetRelativePath(root, path).Replace('\\', '/'), File.ReadAllBytes, StringComparer.Ordinal);
        // AssetLocation canonicalizes case, including generated variant hashes. Keep the raw
        // file namespace exact while sharing its immutable payloads with the asset API view.
        assets = new(() => files.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase));
    }

    /// <summary>Exposes immutable bytes for loaders that accept borrowed spans.</summary>
    internal ReadOnlySpan<byte> Read(string path) => files[Normalize(path)];

    /// <summary>Provides an isolated mutable payload for APIs or fault-injection tests that accept arrays.</summary>
    internal byte[] Copy(string path) => files[Normalize(path)].ToArray();

    /// <summary>Returns an isolated asset or a deliberate missing-asset result without touching disk.</summary>
    internal byte[]? TryCopy(string path) => files.TryGetValue(Normalize(path), out var bytes) ? bytes.ToArray() : null;

    /// <summary>Matches canonicalized engine asset identities while retaining isolated returned payloads.</summary>
    internal byte[]? TryCopyAsset(string path) => assets.Value.TryGetValue(Normalize(path), out var bytes) ? bytes.ToArray() : null;

    /// <summary>Decodes a captured source input for CPU preprocessing assertions.</summary>
    internal string Text(string path) => text.GetOrAdd(Normalize(path), name =>
    {
        // Match the source reader's BOM handling without reopening immutable build files.
        using var input = new MemoryStream(files[name], writable: false);
        using var reader = new StreamReader(input, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
        return reader.ReadToEnd();
    });

    /// <summary>Returns captured source text or the source helper's explicit missing-input result.</summary>
    internal string? TryText(string path) => files.ContainsKey(Normalize(path)) ? Text(path) : null;

    /// <summary>Preserves every authored baseline row, including empty rows, without a filesystem read.</summary>
    internal string[] Lines(string path)
    {
        using var reader = new StringReader(Text(path));
        var rows = new List<string>();
        while (reader.ReadLine() is { } line) rows.Add(line);
        return rows.ToArray();
    }

    /// <summary>Enumerates captured relative names without probing the filesystem.</summary>
    internal IEnumerable<string> Paths => files.Keys;

    /// <summary>Maps a build-output absolute path to isolated bytes without a runtime filesystem read.</summary>
    internal static byte[] ReadBuiltBytes(string path) => Built.Copy(Path.GetRelativePath(
        Path.Combine(AppContext.BaseDirectory, "assets"), path));

    /// <summary>Maps a build-output absolute source path to its captured text.</summary>
    internal static string ReadBuiltText(string path) => Built.Text(Path.GetRelativePath(
        Path.Combine(AppContext.BaseDirectory, "assets"), path));

    /// <summary>Reads a build-frozen owned or engine shader source without inspecting its installed directory at test time.</summary>
    internal static string ReadSourceText(string path) => Built.Text(SourceKey(path));

    /// <summary>Checks build-frozen fixture availability in memory.</summary>
    internal static bool HasFile(string path) => Built.files.ContainsKey(SourceKey(path));

    /// <summary>Checks captured directory membership without per-case filesystem probes.</summary>
    internal static bool HasDirectory(string path)
    {
        string key = SourceKey(path).TrimEnd('/') + "/";
        return Built.Paths.Any(name => name.StartsWith(key, StringComparison.Ordinal));
    }
    #endregion

    #region Private
    /// <summary>Uses the build's input manifest so stale copied sources and removed binaries cannot enter a new test process.</summary>
    private static TestAssetSnapshot LoadBuilt()
    {
        string root = Path.Combine(AppContext.BaseDirectory, "assets");
        var paths = File.ReadAllLines(Path.Combine(AppContext.BaseDirectory, "fixture-inputs.txt"))
            .Select(Normalize).Where(path => path.StartsWith("assets/", StringComparison.Ordinal))
            .Select(path => Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, path)));
        return new(root, paths);
    }

    /// <summary>Uses the asset API's platform-independent path spelling.</summary>
    private static string Normalize(string path) => path.Replace('\\', '/');

    /// <summary>Maps installed and owned source names to the build's explicit asset overlay.</summary>
    private static string SourceKey(string path)
    {
        string normalized = Normalize(path);
        const string game = "/assets/game/", owned = "/assets/vanillagraphicsexpanded/";
        int marker = normalized.IndexOf(game, StringComparison.OrdinalIgnoreCase);
        if (marker >= 0) return "game/" + normalized[(marker + game.Length)..];
        marker = normalized.IndexOf(owned, StringComparison.OrdinalIgnoreCase);
        if (marker >= 0) return normalized[(marker + owned.Length)..];
        return Normalize(Path.GetRelativePath(Path.Combine(AppContext.BaseDirectory, "assets"), path));
    }
    #endregion
}
