using System.Diagnostics;

namespace ShaderBuildTool.Spirv;

/// <summary>Owns one compiler process tree and captures both diagnostic streams without pipe deadlocks.</summary>
internal static class ShaderCompilerProcess
{
    #region Process execution
    /// <summary>Runs the pinned shader compiler, preserving names and the existing command-line contract.</summary>
    public static Task<ShaderCompilerResult> CompileAsync(string workingDirectory, string input, string output,
        string stage, string target, bool warningsAsErrors, string entryPoint, CancellationToken cancellationToken)
    {
        var start = new ProcessStartInfo("dotnet") { WorkingDirectory = workingDirectory };
        string[] arguments = ["tool", "run", "dotnet-shaderc", "--", "--shader-stage=" + stage,
            "--entry-point=" + entryPoint, "--target-env=" + target, "-g", "-x=glsl"];
        foreach (string argument in arguments) start.ArgumentList.Add(argument);
        if (warningsAsErrors) start.ArgumentList.Add("-Werror");
        start.ArgumentList.Add("-o");
        start.ArgumentList.Add(output);
        start.ArgumentList.Add(input);
        return RunAsync(start, cancellationToken);
    }

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
