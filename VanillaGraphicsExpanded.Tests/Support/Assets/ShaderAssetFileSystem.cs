using VanillaGraphicsExpanded.Cache.Disk;

namespace VanillaGraphicsExpanded.Tests.Support.Assets;

/// <summary>Supplies immutable built assets through a strict read-only filesystem test provider.</summary>
internal sealed class ShaderAssetFileSystem : IDiskCacheFileSystem
{
    internal static IDiskCacheFileSystem Built { get; } = new ShaderAssetFileSystem();

    #region Public API
    /// <summary>Reads an isolated build payload and rejects paths outside the captured input set.</summary>
    public byte[] ReadAllBytes(string path)
    {
        string relative = Path.GetRelativePath(Path.Combine(AppContext.BaseDirectory, "assets"), path).Replace('\\', '/');
        return TestAssetSnapshot.Built.TryCopy(relative)
            ?? throw new FileNotFoundException("Missing build asset: " + relative, path);
    }
    #endregion

    #region Interface contract
    // Only immutable reads are part of this loader fixture. Unexpected persistence or probes
    // fail explicitly rather than falling through to a real filesystem.
    /// <inheritdoc />
    bool IDiskCacheFileSystem.DirectoryExists(string path) => throw new NotSupportedException();
    /// <inheritdoc />
    void IDiskCacheFileSystem.CreateDirectory(string path) => throw new NotSupportedException();
    /// <inheritdoc />
    bool IDiskCacheFileSystem.FileExists(string path) => throw new NotSupportedException();
    /// <inheritdoc />
    Stream IDiskCacheFileSystem.OpenRead(string path) => throw new NotSupportedException();
    /// <inheritdoc />
    Stream IDiskCacheFileSystem.OpenWrite(string path) => throw new NotSupportedException();
    /// <inheritdoc />
    void IDiskCacheFileSystem.MoveFile(string sourcePath, string destPath, bool overwrite) => throw new NotSupportedException();
    /// <inheritdoc />
    void IDiskCacheFileSystem.DeleteFile(string path) => throw new NotSupportedException();
    /// <inheritdoc />
    void IDiskCacheFileSystem.DeleteDirectory(string path, bool recursive) => throw new NotSupportedException();
    /// <inheritdoc />
    long IDiskCacheFileSystem.GetFileSizeBytes(string path) => throw new NotSupportedException();
    #endregion

    #region Private
    /// <summary>Restricts construction to the shared immutable fixture source.</summary>
    private ShaderAssetFileSystem() { }
    #endregion
}