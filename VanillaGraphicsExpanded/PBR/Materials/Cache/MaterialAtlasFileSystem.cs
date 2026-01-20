using System;
using System.IO;

namespace VanillaGraphicsExpanded.PBR.Materials.Cache;

internal interface IMaterialAtlasFileSystem
{
    void CreateDirectory(string? path);

    bool DirectoryExists(string path);

    void DeleteDirectory(string path, bool recursive);

    bool FileExists(string path);

    byte[] ReadAllBytes(string path);

    void WriteAllBytes(string path, ReadOnlySpan<byte> bytes);

    Stream OpenRead(string path);

    Stream OpenWrite(string path);

    long GetFileLength(string path);

    void DeleteFile(string path);

    void MoveFile(string sourcePath, string destPath, bool overwrite);

    void ReplaceFile(string sourcePath, string destPath);
}

internal sealed class MaterialAtlasRealFileSystem : IMaterialAtlasFileSystem
{
    public void CreateDirectory(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return;
        Directory.CreateDirectory(path);
    }

    public bool DirectoryExists(string path)
        => Directory.Exists(path);

    public void DeleteDirectory(string path, bool recursive)
        => Directory.Delete(path, recursive);

    public bool FileExists(string path)
        => File.Exists(path);

    public byte[] ReadAllBytes(string path)
        => File.ReadAllBytes(path);

    public void WriteAllBytes(string path, ReadOnlySpan<byte> bytes)
    {
        CreateDirectory(Path.GetDirectoryName(path));
        File.WriteAllBytes(path, bytes.ToArray());
    }

    public Stream OpenRead(string path)
        => new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);

    public Stream OpenWrite(string path)
    {
        CreateDirectory(Path.GetDirectoryName(path));
        return new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None);
    }

    public long GetFileLength(string path)
        => new FileInfo(path).Length;

    public void DeleteFile(string path)
        => File.Delete(path);

    public void MoveFile(string sourcePath, string destPath, bool overwrite)
    {
        CreateDirectory(Path.GetDirectoryName(destPath));
        File.Move(sourcePath, destPath, overwrite);
    }

    public void ReplaceFile(string sourcePath, string destPath)
    {
        CreateDirectory(Path.GetDirectoryName(destPath));
        File.Replace(sourcePath, destPath, destinationBackupFileName: null, ignoreMetadataErrors: true);
    }
}
