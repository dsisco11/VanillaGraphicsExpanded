using System.Diagnostics;
using ShaderBuildTool.Spirv;

namespace ShaderBuildTool.Tests.Unit.Spirv.Compiler;

/// <summary>Verifies pre-cancelled requests do not start compiler processes.</summary>
public sealed class ShaderCompilerCancellationGuardTests
{

    #region Execution and cancellation

    /// <summary>Already-cancelled requests cannot launch a child process.</summary>
    [Fact]
    public async Task PreCancelledRequestDoesNotStart()
    {
        using var cancel = new CancellationTokenSource();
        cancel.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            ShaderCompilerProcess.RunAsync(new ProcessStartInfo("nonexistent-vge-compiler"), cancel.Token));
    }
    #endregion
}
