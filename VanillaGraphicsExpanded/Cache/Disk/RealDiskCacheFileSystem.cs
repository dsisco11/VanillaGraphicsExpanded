using System.IO;
using System.Collections.Generic;

namespace VanillaGraphicsExpanded.Cache.Disk;

/// <summary>Supplies physical cache files and same-filesystem atomic publication.</summary>
internal sealed class RealDiskCacheFileSystem : IAtomicCacheFileSystem
{
    #region Public API
    #region Atomic publication
    /// <summary>Holds the cache's process lease without truncating existing contents.</summary>
    public Stream AcquireExclusive(string path) => new FileStream(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);

    /// <summary>Enumerates immediate cache files for cleanup and eviction.</summary>
    public IEnumerable<string> EnumerateFiles(string directory, string pattern) => Directory.EnumerateFiles(directory, pattern);

    /// <summary>Publishes a complete file through the platform's same-filesystem replace operation.</summary>
    public void ReplaceFile(string source, string destination) => File.Move(source, destination, true);
    #endregion

    #region Files and directories
    /// <inheritdoc />
    public bool DirectoryExists(string path) => Directory.Exists(path);

    /// <inheritdoc />
    public void CreateDirectory(string path) => Directory.CreateDirectory(path);

    /// <inheritdoc />
    public bool FileExists(string path) => File.Exists(path);

    /// <inheritdoc />
    public byte[] ReadAllBytes(string path) => File.ReadAllBytes(path);

    /// <inheritdoc />
    public Stream OpenRead(string path) => File.Open(path, FileMode.Open, FileAccess.Read, FileShare.Read);

    /// <inheritdoc />
    public Stream OpenWrite(string path) => File.Open(path, FileMode.Create, FileAccess.Write, FileShare.None);

    /// <inheritdoc />
    public void MoveFile(string sourcePath, string destPath, bool overwrite)
    {
        if (overwrite && File.Exists(destPath))
        {
            File.Delete(destPath);
        }

        File.Move(sourcePath, destPath);
    }

    /// <inheritdoc />
    public void DeleteFile(string path)
    {
        if (File.Exists(path))
        {
            File.Delete(path);
        }
    }

    /// <inheritdoc />
    public void DeleteDirectory(string path, bool recursive)
    {
        if (Directory.Exists(path))
        {
            Directory.Delete(path, recursive);
        }
    }

    /// <inheritdoc />
    public long GetFileSizeBytes(string path) => new FileInfo(path).Length;
    #endregion
    #endregion
}
