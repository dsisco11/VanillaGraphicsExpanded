using ShaderBuildTool.Spirv;

namespace ShaderBuildTool.Tests;

/// <summary>Guards writer exclusion across output cleanup and subsequent incremental builds.</summary>
public sealed class ShaderOutputLeaseTests
{
    #region Ownership
    /// <summary>Cleaning a leased output cannot unlock it; disposal allows a new build to own the same path.</summary>
    [Fact]
    public void LeaseSurvivesOutputCleanupAndCanBeReacquired()
    {
        string parent = Path.Combine(Path.GetTempPath(), "vge-shader-lease-" + Guid.NewGuid().ToString("N"));
        string output = Path.Combine(parent, "output");
        try
        {
            Directory.CreateDirectory(output);
            using (ShaderOutputLease.Acquire(output))
            {
                Directory.Delete(output);
                Assert.Throws<IOException>(() => ShaderOutputLease.Acquire(output + Path.DirectorySeparatorChar));
                using var separate = ShaderOutputLease.Acquire(Path.Combine(parent, "other-output"));
            }
            using var reacquired = ShaderOutputLease.Acquire(output);
        }
        finally { Directory.Delete(parent, recursive: true); }
    }
    #endregion
}
