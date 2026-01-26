using System.IO;

namespace VanillaGraphicsExpanded.Cache.Disk;

internal interface IDiskCacheFileSystem
{
    bool DirectoryExists(string path);

    void CreateDirectory(string path);

    bool FileExists(string path);

    byte[] ReadAllBytes(string path);

    Stream OpenRead(string path);

    Stream OpenWrite(string path);

    void MoveFile(string sourcePath, string destPath, bool overwrite);

    void DeleteFile(string path);

    void DeleteDirectory(string path, bool recursive);

    long GetFileSizeBytes(string path);
}
