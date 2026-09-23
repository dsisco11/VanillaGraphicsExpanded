using System.Diagnostics;
using ShaderBuildTool.Spirv;

namespace ShaderBuildTool.Tests;

/// <summary>Exercises the actual process owner with controlled PowerShell children, without invoking shaderc.</summary>
public sealed class ShaderCompilerProcessTests
{
    #region Process fixtures
    /// <summary>Creates a hidden Windows process with an encoded script so argument quoting cannot alter the fixture.</summary>
    private static ProcessStartInfo Script(string script)
    {
        var start = new ProcessStartInfo("powershell.exe");
        start.ArgumentList.Add("-NoProfile");
        start.ArgumentList.Add("-NonInteractive");
        start.ArgumentList.Add("-EncodedCommand");
        start.ArgumentList.Add(Convert.ToBase64String(System.Text.Encoding.Unicode.GetBytes(script)));
        return start;
    }

    /// <summary>Queries a recorded fixture PID, treating a reaped process as stopped.</summary>
    private static bool IsRunning(int id)
    {
        try { using var process = Process.GetProcessById(id); return !process.HasExited; }
        catch (ArgumentException) { return false; }
    }
    #endregion

    #region Execution and cancellation
    /// <summary>Large simultaneous stdout/stderr streams cannot deadlock; a nonzero exit remains observable.</summary>
    [Fact]
    public async Task DrainsBothPipesAndPreservesExitCode()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var result = await ShaderCompilerProcess.RunAsync(Script(
            "[Console]::Out.Write('o' * 131072); [Console]::Error.Write('e' * 131072); exit 7"), timeout.Token);
        Assert.Equal(7, result.ExitCode);
        Assert.Equal(new string('o', 131072), result.StandardOutput);
        Assert.Equal(new string('e', 131072), result.StandardError);
    }

    /// <summary>Cancellation reaps the launcher and its child rather than leaving background compiler work alive.</summary>
    [Fact]
    public async Task CancellationTerminatesProcessTree()
    {
        string marker = Path.Combine(Path.GetTempPath(), "vge-compiler-child-" + Guid.NewGuid().ToString("N"));
        using var cancel = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        int[] ids = [];
        // Marker is written only after the child exists, making cancellation deterministic.
        string quoted = marker.Replace("'", "''");
        var running = ShaderCompilerProcess.RunAsync(Script(
            "$child = Start-Process powershell.exe -WindowStyle Hidden -PassThru -ArgumentList '-NoProfile','-NonInteractive','-Command','Start-Sleep -Seconds 120'; " +
            $"[IO.File]::WriteAllText('{quoted}', [string]$PID + ',' + [string]$child.Id); Start-Sleep -Seconds 120"), cancel.Token);
        try
        {
            while (!File.Exists(marker)) await Task.Delay(20, cancel.Token);
            // A new file may be observed between creation and completion of the write.
            string text;
            do { await Task.Delay(20, cancel.Token); text = await File.ReadAllTextAsync(marker, cancel.Token); }
            while (text.Split(',').Length != 2);
            ids = text.Split(',').Select(int.Parse).ToArray();
            cancel.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => running);
            Assert.All(ids, id => Assert.False(IsRunning(id), $"Fixture process {id} survived cancellation."));
        }
        finally
        {
            cancel.Cancel();
            try { await running; } catch (OperationCanceledException) { }
            // If the assertion fails, leave no fixture children behind while retaining the failure.
            foreach (int id in ids)
            {
                try { using var process = Process.GetProcessById(id); if (!process.HasExited) process.Kill(true); }
                catch (ArgumentException) { }
            }
            File.Delete(marker);
        }
    }

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
