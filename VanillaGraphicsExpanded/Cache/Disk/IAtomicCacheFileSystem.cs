using System.IO;
using System.Collections.Generic;

namespace VanillaGraphicsExpanded.Cache.Disk;

/// <summary>Supplies executable-cache persistence, atomic publication and nonblocking exclusive leases.</summary>
internal interface IAtomicCacheFileSystem : IDiskCacheFileSystem
{
    #region Public API
    /// <summary>Acquires a nonblocking exclusive lease until disposal, or throws on contention.</summary>
    Stream AcquireExclusive(string path);
    /// <summary>Enumerates immediate files matching a filename pattern.</summary>
    IEnumerable<string> EnumerateFiles(string directory, string pattern);
    /// <summary>Atomically replaces the destination with a completed same-directory temporary file.</summary>
    void ReplaceFile(string source, string destination);
    #endregion
}
