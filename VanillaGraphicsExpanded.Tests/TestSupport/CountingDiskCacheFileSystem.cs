using System;
using System.Collections.Generic;
using System.IO;

using VanillaGraphicsExpanded.Cache.Disk;

namespace VanillaGraphicsExpanded.Tests.TestSupport;

internal sealed class CountingDiskCacheFileSystem : IDiskCacheFileSystem
{
    private readonly HashSet<string> directories = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, byte[]> files = new(StringComparer.OrdinalIgnoreCase);

    public int MetaJsonCommitWrites { get; private set; }

    private static string Normalize(string path)
        => path.Replace('\\', '/');

    public bool DirectoryExists(string path)
        => directories.Contains(Normalize(path));

    public void CreateDirectory(string path)
    {
        string p = Normalize(path);
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

    public bool FileExists(string path)
        => files.ContainsKey(Normalize(path));

    public byte[] ReadAllBytes(string path)
        => files[Normalize(path)];

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
        CreateDirectory(Path.GetDirectoryName(p) ?? string.Empty);
        return new CommitOnDisposeStream(bytes => files[p] = bytes, initialCapacity: 256);
    }

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

    public void DeleteFile(string path)
        => files.Remove(Normalize(path));

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

    public long GetFileSizeBytes(string path)
        => files.TryGetValue(Normalize(path), out byte[]? data) ? data.LongLength : 0;

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
