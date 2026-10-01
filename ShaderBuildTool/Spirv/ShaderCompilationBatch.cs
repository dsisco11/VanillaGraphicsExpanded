namespace ShaderBuildTool.Spirv;

/// <summary>A uniquely addressed compiler job whose mutable state belongs to that invocation.</summary>
internal sealed record ShaderCompilationJob(string Identity, Func<CancellationToken, Task<ShaderCompilerResult>> Execute);

/// <summary>Schedules bounded compiler jobs and reports captured diagnostics in stable input order.</summary>
internal static class ShaderCompilationBatch
{
    #region Scheduling
    /// <summary>Cancels outstanding work on the first failure and awaits every worker before reporting completion.</summary>
    public static async Task RunAsync(IReadOnlyList<ShaderCompilationJob> jobs, int concurrency,
        TextWriter output, TextWriter error, CancellationToken cancellationToken)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(concurrency, 1);
        if (jobs.Select(job => job.Identity).Distinct(StringComparer.Ordinal).Count() != jobs.Count)
            throw new ArgumentException("Duplicate shader compilation job identities.", nameof(jobs));
        using var stop = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var results = new ShaderCompilerResult?[jobs.Count];
        var failures = new Exception?[jobs.Count];
        int next = -1;
        int completed = 0;
        var progressTimer = System.Diagnostics.Stopwatch.StartNew();
        var progressLock = new object();
        // Fixed workers bound both AST emission and external processes, rather than starting one task per variant.
        var workers = Enumerable.Range(0, Math.Min(concurrency, jobs.Count)).Select(_ => Task.Run(async () =>
        {
            while (!stop.IsCancellationRequested)
            {
                int index = Interlocked.Increment(ref next);
                if (index >= jobs.Count) break;
                try
                {
                    stop.Token.ThrowIfCancellationRequested();
                    results[index] = await jobs[index].Execute(stop.Token);
                    // Serialize sparse progress messages so parallel workers cannot interleave counts.
                    lock (progressLock)
                    {
                        completed++;
                        if (progressTimer.Elapsed.TotalSeconds >= 5 || completed == jobs.Count)
                        {
                            output.WriteLine($"[SPIR-V] Variant progress: {completed}/{jobs.Count} processed.");
                            progressTimer.Restart();
                        }
                    }
                    if (results[index]!.ExitCode != 0) stop.Cancel();
                }
                catch (OperationCanceledException) when (stop.IsCancellationRequested) { }
                catch (Exception failure)
                {
                    failures[index] = failure;
                    stop.Cancel();
                }
            }
        })).ToArray();
        await Task.WhenAll(workers);

        bool failed = false;
        for (int index = 0; index < jobs.Count; index++)
        {
            var result = results[index];
            if (result is not null)
            {
                if (!string.IsNullOrWhiteSpace(result.StandardOutput))
                    output.WriteLine($"[SPIR-V] {jobs[index].Identity}\n{result.StandardOutput.TrimEnd()}");
                if (!string.IsNullOrWhiteSpace(result.StandardError))
                    error.WriteLine($"[SPIR-V] {jobs[index].Identity}\n{result.StandardError.TrimEnd()}");
            }
            if (failures[index] is not null || result?.ExitCode is not null and not 0)
            {
                failed = true;
                error.WriteLine($"[SPIR-V] Failed {jobs[index].Identity}: {failures[index]?.Message ?? $"compiler exit {result!.ExitCode}"}");
            }
        }
        if (failed) throw new InvalidOperationException("Shader compilation failed; no build receipt was published.");
        cancellationToken.ThrowIfCancellationRequested();
    }
    #endregion
}
