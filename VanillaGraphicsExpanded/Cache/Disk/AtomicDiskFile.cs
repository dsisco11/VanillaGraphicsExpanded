using System;
using System.IO;

namespace VanillaGraphicsExpanded.Cache.Disk;

internal static class AtomicDiskFile
{
    public static bool TryWriteAtomic(IDiskCacheFileSystem fileSystem, string targetPath, ReadOnlySpan<byte> bytes)
    {
        ArgumentNullException.ThrowIfNull(fileSystem);

        try
        {
            string directory = Path.GetDirectoryName(targetPath) ?? string.Empty;
            if (!string.IsNullOrEmpty(directory) && !fileSystem.DirectoryExists(directory))
            {
                fileSystem.CreateDirectory(directory);
            }

            string tmp = targetPath + ".tmp";
            using (Stream s = fileSystem.OpenWrite(tmp))
            {
                s.Write(bytes);
                s.Flush();
            }

            fileSystem.MoveFile(tmp, targetPath, overwrite: true);
            return true;
        }
        catch
        {
            try
            {
                fileSystem.DeleteFile(targetPath + ".tmp");
            }
            catch
            {
                // Best-effort.
            }

            return false;
        }
    }
}
