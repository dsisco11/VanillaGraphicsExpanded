using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

using VanillaGraphicsExpanded.PBR.Materials.Cache;

namespace VanillaGraphicsExpanded.Tests.TestSupport;

internal sealed class InMemoryMaterialAtlasFileSystem : IMaterialAtlasFileSystem
{
    private readonly HashSet<string> directories = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, byte[]> files = new(StringComparer.OrdinalIgnoreCase);

    private static string Normalize(string path)
        => path.Replace('\\', '/');

    private static string? NormalizeOrNull(string? path)
        => path is null ? null : Normalize(path);

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

    public bool DirectoryExists(string path)
    {
        string p = Normalize(path);
        return directories.Contains(p) || files.Keys.Any(f => f.StartsWith(p.TrimEnd('/') + "/", StringComparison.OrdinalIgnoreCase));
    }

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

    public bool FileExists(string path)
        => files.ContainsKey(Normalize(path));

    public byte[] ReadAllBytes(string path)
        => files[Normalize(path)];

    public void WriteAllBytes(string path, ReadOnlySpan<byte> bytes)
    {
        string p = Normalize(path);
        CreateDirectory(Path.GetDirectoryName(p));
        files[p] = bytes.ToArray();
    }

    public Stream OpenRead(string path)
    {
        string p = Normalize(path);
        if (!files.TryGetValue(p, out byte[]? data))
        {
            throw new FileNotFoundException("File not found", p);
        }

        return new MemoryStream(data, writable: false);
    }

    public Stream OpenWrite(string path)
    {
        string p = Normalize(path);
        CreateDirectory(Path.GetDirectoryName(p));

        return new CommitOnDisposeStream(bytes => files[p] = bytes, initialCapacity: 1024);
    }

    public long GetFileLength(string path)
        => files.TryGetValue(Normalize(path), out byte[]? data) ? data.LongLength : 0;

    public void DeleteFile(string path)
        => files.Remove(Normalize(path));

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

    private sealed class CommitOnDisposeStream : MemoryStream
    {
        private readonly Action<byte[]> onCommit;

        public CommitOnDisposeStream(Action<byte[]> onCommit, int initialCapacity)
            : base(initialCapacity)
        {
            this.onCommit = onCommit;
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                onCommit(ToArray());
            }

            base.Dispose(disposing);
        }
    }
}
