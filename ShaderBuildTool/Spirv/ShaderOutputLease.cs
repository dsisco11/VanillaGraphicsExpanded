namespace ShaderBuildTool.Spirv;

/// <summary>Excludes simultaneous writers before receipt validation, cleanup or shader publication.</summary>
internal static class ShaderOutputLease
{
    #region Output ownership
    /// <summary>Acquires an adjacent persistent lock file so cleaning the output cannot release ownership.</summary>
    public static FileStream Acquire(string outputRoot)
    {
        string path = Path.TrimEndingDirectorySeparator(Path.GetFullPath(outputRoot)) + ".lock";
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        try
        {
            // Keep the file after disposal: unlinking a lock can let two writers own different handles.
            return new FileStream(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        }
        catch (IOException error)
        {
            throw new IOException($"Cannot acquire shader output '{outputRoot}'; another build may own it.", error);
        }
    }
    #endregion
}
