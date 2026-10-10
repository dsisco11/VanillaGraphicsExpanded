using System.Runtime.InteropServices;

namespace ShaderBuildTool.Spirv;

/// <summary>Reuses immutable verified binary files while retaining a portable copy fallback.</summary>
internal static class ShaderBinaryReuse
{
    #region Public API
    /// <summary>Attempts an NTFS hard link; unsupported or cross-volume paths use the caller's copy fallback.</summary>
    internal static bool TryLink(string destination, string source)
    {
        if (!OperatingSystem.IsWindows()) return false;
        return CreateHardLink(destination, source, IntPtr.Zero);
    }
    #endregion

    #region Private
    /// <summary>Creates another directory entry for an immutable file without copying its contents.</summary>
    [DllImport("kernel32.dll", EntryPoint = "CreateHardLinkW", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CreateHardLink(string destination, string source, IntPtr securityAttributes);
    #endregion
}
