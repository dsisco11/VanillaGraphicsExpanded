using System.IO;

namespace VanillaGraphicsExpanded.Cache.Disk;

internal sealed class RealDiskCacheFileSystem : IDiskCacheFileSystem
{
    public bool DirectoryExists(string path) => Directory.Exists(path);

    public void CreateDirectory(string path) => Directory.CreateDirectory(path);

    public bool FileExists(string path) => File.Exists(path);

    public byte[] ReadAllBytes(string path) => File.ReadAllBytes(path);

    public Stream OpenRead(string path) => File.Open(path, FileMode.Open, FileAccess.Read, FileShare.Read);

    public Stream OpenWrite(string path) => File.Open(path, FileMode.Create, FileAccess.Write, FileShare.None);

    public void MoveFile(string sourcePath, string destPath, bool overwrite)
    {
        if (overwrite && File.Exists(destPath))
        {
            File.Delete(destPath);
        }

        File.Move(sourcePath, destPath);
    }

    public void DeleteFile(string path)
    {
        if (File.Exists(path))
        {
            File.Delete(path);
        }
    }

    public void DeleteDirectory(string path, bool recursive)
    {
        if (Directory.Exists(path))
        {
            Directory.Delete(path, recursive);
        }
    }

    public long GetFileSizeBytes(string path) => new FileInfo(path).Length;
}
