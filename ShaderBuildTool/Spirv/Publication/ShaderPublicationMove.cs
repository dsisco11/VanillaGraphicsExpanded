namespace ShaderBuildTool.Spirv;

/// <summary>Retries short Windows rename conflicts without weakening generation-level publication or recovery.</summary>
internal static class ShaderPublicationMove
{
    #region Public API
    /// <summary>Moves a generation atomically, with a bounded cancellable delay for Windows sharing/access conflicts.</summary>
    internal static void Move(string source, string destination, Action<string>? report = null,
        CancellationToken cancellationToken = default)
    {
        // Directory rename can report access denied rather than sharing violation when a
        // reader holds a descendant. Never fall back to partial in-place file replacement.
        for (int attempt = 0; ; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try { Directory.Move(source, destination); return; }
            catch (Exception error) when (IsWindowsConflict(error))
            {
                int code = error.HResult & 0xffff;
                if (attempt == 5)
                    throw new IOException($"Cannot publish shader generation: rename '{source}' to '{destination}' " +
                        $"remained blocked after 6 attempts (Windows error {code}, HRESULT 0x{error.HResult:X8}). " +
                        "Inspect open handles to this directory and its descendants, and directory permissions. " +
                        "No per-file replacement was attempted; the uncommitted transaction will undergo normal publication recovery.", error);
                int delay = 50 << attempt;
                // MSBuild Exec recognizes "error <number>" inside ordinary output as a failure.
                // A bounded retry is informational; exhausted attempts still throw the real diagnostic above.
                report?.Invoke($"Shader directory rename blocked (Windows status {code}): '{source}' -> '{destination}'; " +
                    $"retry {attempt + 1}/5 in {delay} ms.");
                if (cancellationToken.WaitHandle.WaitOne(delay)) cancellationToken.ThrowIfCancellationRequested();
            }
        }
    }
    #endregion

    #region Private
    /// <summary>Limits retries to Windows access/sharing/lock violations, preserving other failures immediately.</summary>
    private static bool IsWindowsConflict(Exception error) => OperatingSystem.IsWindows() &&
        error is IOException or UnauthorizedAccessException && (error.HResult & 0xffff) is 5 or 32 or 33;
    #endregion
}
