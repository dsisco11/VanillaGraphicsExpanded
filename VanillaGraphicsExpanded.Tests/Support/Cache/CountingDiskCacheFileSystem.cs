using System;
using System.Collections.Generic;
using System.IO;

using VanillaGraphicsExpanded.Cache.Disk;

namespace VanillaGraphicsExpanded.Tests.Support.Cache;

/// <summary>Models isolated cache files, leases and atomic publications entirely in memory.</summary>
internal sealed class CountingDiskCacheFileSystem : IAtomicCacheFileSystem
{
    private readonly Action<string, byte[]>? published;
    private readonly HashSet<string> leases = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> directories = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, byte[]> files = new(StringComparer.OrdinalIgnoreCase);

    #region Public API
    /// <summary>Optionally observes completed diagnostic reports while retaining all storage in memory.</summary>
    internal CountingDiskCacheFileSystem(Action<string, byte[]>? published = null)
    {
        this.published = published;
    }

    public int MetaJsonCommitWrites { get; private set; }

    #region Atomic publication
    /// <summary>Models exclusive ownership without creating a physical lock file.</summary>
    public Stream AcquireExclusive(string path)
    {
        string key = Normalize(path);
        lock (leases)
            if (!leases.Add(key)) throw new IOException("The cache lease is already held.");
        return new CommitOnDisposeStream(_ => { lock (leases) leases.Remove(key); }, 0);
    }

    /// <summary>Enumerates an isolated immediate-directory snapshot for eviction and corruption tests.</summary>
    public IEnumerable<string> EnumerateFiles(string directory, string pattern)
    {
        string root = Normalize(directory).TrimEnd('/');
        return files.Keys.Where(path => Normalize(Path.GetDirectoryName(path) ?? "") == root
            && System.IO.Enumeration.FileSystemName.MatchesSimpleExpression(pattern, Path.GetFileName(path))).ToArray();
    }

    /// <summary>Publishes a completed payload as one in-memory replacement operation.</summary>
    public void ReplaceFile(string source, string destination) => MoveFile(source, destination, true);

    #endregion

    #region Files and directories
    /// <summary>Reports an explicitly created in-memory directory.</summary>
    public bool DirectoryExists(string path)
        => directories.Contains(Normalize(path));

    /// <summary>Creates a fixture directory and its parents, rejecting a file occupying the requested path.</summary>
    public void CreateDirectory(string path)
    {
        string p = Normalize(path);
        if (files.ContainsKey(p)) throw new IOException("A file occupies the directory path.");
        if (string.IsNullOrWhiteSpace(p)) return;
        directories.Add(p);

        string? parent = Path.GetDirectoryName(p);
        while (!string.IsNullOrWhiteSpace(parent))
        {
            string pn = Normalize(parent);
            if (!directories.Add(pn)) break;
            parent = Path.GetDirectoryName(parent);
        }
    }

    /// <summary>Reports whether controlled bytes exist under a path.</summary>
    public bool FileExists(string path)
        => files.ContainsKey(Normalize(path));

    /// <summary>Returns isolated bytes or the ordinary missing-file outcome.</summary>
    public byte[] ReadAllBytes(string path)
        => files.TryGetValue(Normalize(path), out var bytes) ? bytes.ToArray() : throw new FileNotFoundException($"Could not find file '{path}'.", path);

    /// <summary>Opens immutable fixture bytes for stream-based codecs.</summary>
    public Stream OpenRead(string path)
    {
        string p = Normalize(path);
        if (!files.TryGetValue(p, out byte[]? data))
        {
            throw new FileNotFoundException("File not found", p);
        }

        return new MemoryStream(data, writable: false);
    }

    /// <summary>Commits a memory-backed write when the stream is disposed.</summary>
    public Stream OpenWrite(string path)
    {
        string p = Normalize(path);
        CreateDirectory(Path.GetDirectoryName(p) ?? string.Empty);
        return new CommitOnDisposeStream(bytes =>
        {
            files[p] = bytes;
            published?.Invoke(p, bytes.ToArray());
        }, initialCapacity: 256);
    }

    /// <summary>Moves controlled bytes and counts completed metadata publications.</summary>
    public void MoveFile(string sourcePath, string destPath, bool overwrite)
    {
        string src = Normalize(sourcePath);
        string dst = Normalize(destPath);

        if (!files.TryGetValue(src, out byte[]? data))
        {
            throw new FileNotFoundException("File not found", src);
        }

        if (!overwrite && files.ContainsKey(dst))
        {
            throw new IOException("Destination exists.");
        }

        CreateDirectory(Path.GetDirectoryName(dst) ?? string.Empty);
        files[dst] = data;
        files.Remove(src);

        if (dst.EndsWith("/meta.json", StringComparison.OrdinalIgnoreCase) || dst.EndsWith("meta.json", StringComparison.OrdinalIgnoreCase))
        {
            MetaJsonCommitWrites++;
        }
    }

    /// <summary>Removes a controlled file without physical I/O.</summary>
    public void DeleteFile(string path)
        => files.Remove(Normalize(path));

    /// <summary>Removes only the selected in-memory directory and optionally its descendants.</summary>
    public void DeleteDirectory(string path, bool recursive)
    {
        string p = Normalize(path).TrimEnd('/');

        if (!recursive)
        {
            directories.Remove(p);
            return;
        }

        foreach (string f in new List<string>(files.Keys))
        {
            if (f.StartsWith(p + "/", StringComparison.OrdinalIgnoreCase))
            {
                files.Remove(f);
            }
        }

        foreach (string d in new List<string>(directories))
        {
            if (d.StartsWith(p, StringComparison.OrdinalIgnoreCase))
            {
                directories.Remove(d);
            }
        }
    }

    /// <summary>Reports the controlled payload length.</summary>
    public long GetFileSizeBytes(string path)
        => files.TryGetValue(Normalize(path), out byte[]? data) ? data.LongLength : 0;

    #endregion
    #endregion

    #region Internal API
    /// <summary>Supplies controlled corruption or fixture bytes independently of the store implementation.</summary>
    internal void WriteAllBytes(string path, byte[] bytes)
    {
        using var stream = OpenWrite(path);
        stream.Write(bytes);
    }

    /// <summary>Supplies controlled malformed metadata.</summary>
    internal void WriteAllText(string path, string text) => WriteAllBytes(path, System.Text.Encoding.UTF8.GetBytes(text));
    #endregion

    #region Private
    /// <summary>Normalizes separators for deterministic cache keys.</summary>
    private static string Normalize(string path)
        => path.Replace('\\', '/');

    /// <summary>Publishes a controlled write at stream retirement.</summary>
    private sealed class CommitOnDisposeStream : MemoryStream
    {
        private Action<byte[]>? onCommit;

        /// <summary>Retains the fixture commit callback.</summary>
        public CommitOnDisposeStream(Action<byte[]> onCommit, int initialCapacity)
            : base(initialCapacity)
        {
            this.onCommit = onCommit;
        }

        /// <summary>Publishes the completed bytes before releasing stream storage.</summary>
        protected override void Dispose(bool disposing)
        {
            // A retired stream must never publish twice or release a newer owner's lease.
            try
            {
                if (disposing && System.Threading.Interlocked.Exchange(ref onCommit, null) is { } commit)
                    commit(ToArray());
            }
            finally { base.Dispose(disposing); }
        }
    }
    #endregion
}
