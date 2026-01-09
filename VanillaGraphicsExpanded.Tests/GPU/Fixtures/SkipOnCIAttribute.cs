using System;
using System.Runtime.InteropServices;
using Xunit;

namespace VanillaGraphicsExpanded.Tests.GPU.Fixtures;

/// <summary>
/// Attribute to skip tests when running in a CI environment without a GPU.
/// 
/// Tests marked with this attribute will be skipped when:
/// - The CI environment variable is set to "true"
/// - The GITHUB_ACTIONS environment variable is set to "true"
/// - Running on Linux without Mesa llvmpipe (software renderer)
/// 
/// Usage:
/// <code>
/// [Fact]
/// [SkipOnCI("Requires hardware GPU")]
/// public void MyGpuIntensiveTest()
/// {
///     // Test code that requires real GPU hardware
/// }
/// </code>
/// </summary>
/// <remarks>
/// GPU tests can run on CI with Mesa llvmpipe (software OpenGL renderer).
/// Use this attribute only for tests that specifically require hardware GPU
/// features not supported by software rendering, such as:
/// - GPU timer queries
/// - Hardware-specific behavior verification
/// - Performance benchmarks
/// 
/// Most functional shader tests should work with llvmpipe and should NOT
/// use this attribute, allowing them to run on CI for regression testing.
/// </remarks>
[AttributeUsage(AttributeTargets.Method | AttributeTargets.Class, AllowMultiple = false)]
public sealed class SkipOnCIAttribute : FactAttribute
{
    /// <summary>
    /// Creates a new SkipOnCIAttribute.
    /// </summary>
    /// <param name="reason">The reason why this test should be skipped on CI.</param>
    public SkipOnCIAttribute(string reason = "Test requires hardware GPU (not available in CI)")
    {
        if (IsRunningOnCI())
        {
            Skip = reason;
        }
    }

    /// <summary>
    /// Detects if the test is running in a CI environment.
    /// </summary>
    private static bool IsRunningOnCI()
    {
        // Check common CI environment variables
        if (Environment.GetEnvironmentVariable("CI") == "true")
            return true;
        
        if (Environment.GetEnvironmentVariable("GITHUB_ACTIONS") == "true")
            return true;
        
        if (!string.IsNullOrEmpty(Environment.GetEnvironmentVariable("TF_BUILD")))  // Azure DevOps
            return true;
        
        if (!string.IsNullOrEmpty(Environment.GetEnvironmentVariable("JENKINS_URL")))
            return true;
        
        if (!string.IsNullOrEmpty(Environment.GetEnvironmentVariable("GITLAB_CI")))
            return true;

        return false;
    }

    /// <summary>
    /// Checks if Mesa llvmpipe (software renderer) is available.
    /// Can be used to determine if software GL rendering is possible.
    /// </summary>
    public static bool IsSoftwareRendererAvailable()
    {
        // On Linux, check if we're using Mesa
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            // Mesa llvmpipe sets LIBGL_ALWAYS_SOFTWARE=1 or similar
            var libglSoftware = Environment.GetEnvironmentVariable("LIBGL_ALWAYS_SOFTWARE");
            if (libglSoftware == "1" || libglSoftware == "true")
                return true;
            
            // Check for llvmpipe in MESA environment
            var galliumDriver = Environment.GetEnvironmentVariable("GALLIUM_DRIVER");
            if (galliumDriver?.Contains("llvmpipe", StringComparison.OrdinalIgnoreCase) == true)
                return true;
        }
        
        return false;
    }
}

/// <summary>
/// Theory attribute variant that skips tests when running in CI.
/// </summary>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = false)]
public sealed class SkipOnCITheoryAttribute : TheoryAttribute
{
    /// <summary>
    /// Creates a new SkipOnCITheoryAttribute.
    /// </summary>
    /// <param name="reason">The reason why this test should be skipped on CI.</param>
    public SkipOnCITheoryAttribute(string reason = "Test requires hardware GPU (not available in CI)")
    {
        if (IsRunningOnCI())
        {
            Skip = reason;
        }
    }

    private static bool IsRunningOnCI()
    {
        return Environment.GetEnvironmentVariable("CI") == "true" ||
               Environment.GetEnvironmentVariable("GITHUB_ACTIONS") == "true" ||
               !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("TF_BUILD")) ||
               !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("JENKINS_URL")) ||
               !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("GITLAB_CI"));
    }
}
