using System.Security.Cryptography;
using System.Text;

namespace VanillaGraphicsExpanded.Tests.Helpers;

/// <summary>Exposes memory-backed diagnostic reports through the runner's existing output channel.</summary>
internal static class DiagnosticReports
{
    #region Internal API
    /// <summary>Reports textual results verbatim and identifies binary payloads without creating physical files.</summary>
    internal static void Publish(string path, byte[] bytes)
    {
        var output = TestContext.Current.TestOutputHelper
            ?? throw new InvalidOperationException("Diagnostic reports require an active test output owner.");
        // The runner owns receipt persistence. Test-owned exports remain in their isolated provider.
        output.WriteLine($"Memory report: {path}, bytes={bytes.Length}, sha256={Convert.ToHexString(SHA256.HashData(bytes))}");
        if (Path.GetExtension(path).ToLowerInvariant() is ".json" or ".csv")
            output.WriteLine(Encoding.UTF8.GetString(bytes));
    }
    #endregion
}
