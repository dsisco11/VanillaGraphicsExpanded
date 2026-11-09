using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

using VanillaGraphicsExpanded.PBR.Materials.Cache;

namespace VanillaGraphicsExpanded.Tests.Support.Cache;

/// <summary>Provides isolated in-memory filesystem behavior for material atlas cache tests.</summary>
internal sealed class InMemoryMaterialAtlasFileSystem : IMaterialAtlasFileSystem
{
    private readonly HashSet<string> directories = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, byte[]> files = new(StringComparer.OrdinalIgnoreCase);

    #region Public API
    /// <inheritdoc />
    public void CreateDirectory(string? path)
    {
        string? p = NormalizeOrNull(path);
        if (string.IsNullOrWhiteSpace(p)) return;
        directories.Add(p);

        // Add parents to keep DirectoryExists stable.
        string? parent = Path.GetDirectoryName(p);
        while (!string.IsNullOrWhiteSpace(parent))
        {
            string pn = Normalize(parent);
            if (!directories.Add(pn)) break;
            parent = Path.GetDirectoryName(parent);
        }
    }

    /// <inheritdoc />
    public bool DirectoryExists(string path)
    {
        string p = Normalize(path);
        return directories.Contains(p) || files.Keys.Any(f => f.StartsWith(p.TrimEnd('/') + "/", StringComparison.OrdinalIgnoreCase));
    }

    /// <inheritdoc />
    public void DeleteDirectory(string path, bool recursive)
    {
        string p = Normalize(path).TrimEnd('/');

        if (!recursive)
        {
            if (files.Keys.Any(f => Path.GetDirectoryName(f)?.Equals(p, StringComparison.OrdinalIgnoreCase) == true))
            {
                throw new IOException("Directory not empty.");
            }

            directories.Remove(p);
            return;
        }

        foreach (string f in files.Keys.Where(f => f.StartsWith(p + "/", StringComparison.OrdinalIgnoreCase)).ToArray())
        {
            files.Remove(f);
        }

        foreach (string d in directories.Where(d => d.StartsWith(p, StringComparison.OrdinalIgnoreCase)).ToArray())
        {
            directories.Remove(d);
        }
    }

    /// <inheritdoc />
    public bool FileExists(string path)
        => files.ContainsKey(Normalize(path));

    /// <inheritdoc />
    public byte[] ReadAllBytes(string path)
        => files[Normalize(path)];

    /// <inheritdoc />
    public void WriteAllBytes(string path, ReadOnlySpan<byte> bytes)
    {
        string p = Normalize(path);
        CreateDirectory(Path.GetDirectoryName(p));
        files[p] = bytes.ToArray();
    }

    /// <inheritdoc />
    public Stream OpenRead(string path)
    {
        string p = Normalize(path);
        if (!files.TryGetValue(p, out byte[]? data))
        {
            throw new FileNotFoundException("File not found", p);
        }

        return new MemoryStream(data, writable: false);
    }

    /// <inheritdoc />
    public Stream OpenWrite(string path)
    {
        string p = Normalize(path);
        CreateDirectory(Path.GetDirectoryName(p));

        return new CommitOnDisposeStream(bytes => files[p] = bytes, initialCapacity: 1024);
    }

    /// <inheritdoc />
    public long GetFileLength(string path)
        => files.TryGetValue(Normalize(path), out byte[]? data) ? data.LongLength : 0;

    /// <inheritdoc />
    public void DeleteFile(string path)
        => files.Remove(Normalize(path));

    /// <inheritdoc />
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

        CreateDirectory(Path.GetDirectoryName(dst));
        files[dst] = data;
        files.Remove(src);
    }

    /// <inheritdoc />
    public void ReplaceFile(string sourcePath, string destPath)
    {
        string src = Normalize(sourcePath);
        string dst = Normalize(destPath);

        if (!files.TryGetValue(src, out byte[]? data))
        {
            throw new FileNotFoundException("File not found", src);
        }

        CreateDirectory(Path.GetDirectoryName(dst));
        files[dst] = data;
        files.Remove(src);
    }
    #endregion

    #region Private
    /// <summary>Normalizes path separators to keep in-memory file keys stable.</summary>
    private static string Normalize(string path)
        => path.Replace('\\', '/');

    /// <summary>Preserves <see langword="null"/> paths while normalizing non-null separators.</summary>
    private static string? NormalizeOrNull(string? path)
        => path is null ? null : Normalize(path);

    /// <summary>Commits a completed in-memory write when its owning stream is disposed.</summary>
    private sealed class CommitOnDisposeStream : MemoryStream
    {
        private readonly Action<byte[]> onCommit;

        /// <summary>Creates a write stream that publishes its bytes to the supplied callback.</summary>
        public CommitOnDisposeStream(Action<byte[]> onCommit, int initialCapacity)
            : base(initialCapacity)
        {
            this.onCommit = onCommit;
        }

        /// <summary>Publishes stream contents once the write operation is complete.</summary>
        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                onCommit(ToArray());
            }

            base.Dispose(disposing);
        }
    }
    #endregion
}
