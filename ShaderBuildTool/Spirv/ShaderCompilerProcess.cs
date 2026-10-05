using System.Diagnostics;

namespace ShaderBuildTool.Spirv;

/// <summary>Owns one compiler process tree and captures both diagnostic streams without pipe deadlocks.</summary>
internal static class ShaderCompilerProcess
{
    /// <summary>Enables performance optimization in every build configuration.</summary>
    internal const string OptimizationArgument = "-O";

    /// <summary>Emits source-level debug information only in Debug builds.</summary>
#if DEBUG
    internal static bool GenerateDebugInfo => true;
#else
    internal static bool GenerateDebugInfo => false;
#endif

    #region Public API
    /// <summary>Runs the pinned shader compiler with optimization and configuration-specific debug information.</summary>
    public static async Task<ShaderCompilerResult> CompileAsync(string workingDirectory, string input, string output,
        string stage, string target, bool warningsAsErrors, string entryPoint, CancellationToken cancellationToken)
    {
        var start = new ProcessStartInfo("dotnet") { WorkingDirectory = workingDirectory };
        string[] arguments = ["tool", "run", "dotnet-shaderc", "--", "--shader-stage=" + stage,
            "--entry-point=" + entryPoint, "--target-env=" + target, OptimizationArgument, "-x=glsl"];
        foreach (string argument in arguments) start.ArgumentList.Add(argument);
        if (GenerateDebugInfo) start.ArgumentList.Add("-g");
        if (warningsAsErrors) start.ArgumentList.Add("-Werror");
        start.ArgumentList.Add("-o");
        start.ArgumentList.Add(output);
        start.ArgumentList.Add(input);
        try
        {
            return await RunAsync(start, cancellationToken);
        }
        catch (Exception failure) when (failure is not OperationCanceledException)
        {
            // Preserve process-launch/IO exceptions together with the exact compiler invocation.
            string argumentsText = string.Join(" ", start.ArgumentList.Select(argument => "\"" + argument + "\""));
            throw new InvalidOperationException(
                $"Shader compiler process failed. Working directory: '{workingDirectory}'. Command: {start.FileName} {argumentsText}", failure);
        }
    }

    #endregion

    #region Private
    /// <summary>Drains both pipes concurrently and kills/reaps the child tree before returning on cancellation.</summary>
    internal static async Task<ShaderCompilerResult> RunAsync(ProcessStartInfo start, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        start.RedirectStandardOutput = true;
        start.RedirectStandardError = true;
        start.UseShellExecute = false;
        start.CreateNoWindow = true;
        using var process = Process.Start(start) ?? throw new InvalidOperationException("Failed to start shader compiler.");
        Task<string> stdout = process.StandardOutput.ReadToEndAsync();
        Task<string> stderr = process.StandardError.ReadToEndAsync();
        try
        {
            await process.WaitForExitAsync(cancellationToken);
        }
        catch (OperationCanceledException)
        {
            // A cancelled build must not leave the dotnet tool launcher or its native compiler running.
            try { process.Kill(entireProcessTree: true); }
            catch (InvalidOperationException) { /* The compiler exited between cancellation and termination. */ }
            await process.WaitForExitAsync();
            await Task.WhenAll(stdout, stderr);
            throw;
        }
        return new ShaderCompilerResult(process.ExitCode, await stdout, await stderr);
    }
    #endregion
}

/// <summary>Captured diagnostics and exit status from a single compiler invocation.</summary>
internal sealed record ShaderCompilerResult(int ExitCode, string StandardOutput, string StandardError);
