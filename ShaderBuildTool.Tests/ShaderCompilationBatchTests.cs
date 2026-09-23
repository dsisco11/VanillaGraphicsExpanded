using ShaderBuildTool.Spirv;

namespace ShaderBuildTool.Tests;

/// <summary>Exercises bounded scheduling and deterministic reporting independently of shaderc execution time.</summary>
public sealed class ShaderCompilationBatchTests
{
    #region Scheduling and diagnostics
    /// <summary>Proves the requested bound is reached but never exceeded, with every job executed once.</summary>
    [Theory]
    [InlineData(1)]
    [InlineData(4)]
    public async Task RunsEveryJobExactlyOnceWithinBound(int concurrency)
    {
        int active = 0, peak = 0, started = 0;
        var full = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var calls = new int[12];
        var jobs = Enumerable.Range(0, calls.Length).Select(index => new ShaderCompilationJob($"job-{index}", async token =>
        {
            Interlocked.Increment(ref calls[index]);
            int count = Interlocked.Increment(ref active);
            lock (calls) peak = Math.Max(peak, count);
            if (Interlocked.Increment(ref started) == concurrency) full.SetResult();
            await full.Task.WaitAsync(token);
            await Task.Yield();
            Interlocked.Decrement(ref active);
            return new ShaderCompilerResult(0, index.ToString(), "");
        })).ToArray();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await ShaderCompilationBatch.RunAsync(jobs, concurrency, TextWriter.Null, TextWriter.Null, timeout.Token);
        Assert.Equal(concurrency, peak);
        Assert.All(calls, count => Assert.Equal(1, count));
        Assert.Equal(0, active);
    }

    /// <summary>Deliberately reverses completion order; output still identifies jobs in declaration order.</summary>
    [Fact]
    public async Task ReportsBothStreamsInInputOrder()
    {
        var second = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var jobs = new[]
        {
            new ShaderCompilationJob("first", async token =>
            {
                await second.Task.WaitAsync(token);
                return new ShaderCompilerResult(0, "stdout-a", "stderr-a");
            }),
            new ShaderCompilationJob("second", token =>
            {
                second.SetResult();
                return Task.FromResult(new ShaderCompilerResult(0, "stdout-b", "stderr-b"));
            })
        };
        using var output = new StringWriter();
        using var error = new StringWriter();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await ShaderCompilationBatch.RunAsync(jobs, 2, output, error, timeout.Token);
        Assert.True(output.ToString().IndexOf("stdout-a", StringComparison.Ordinal) < output.ToString().IndexOf("stdout-b", StringComparison.Ordinal));
        Assert.True(error.ToString().IndexOf("stderr-a", StringComparison.Ordinal) < error.ToString().IndexOf("stderr-b", StringComparison.Ordinal));
        Assert.Contains("[SPIR-V] first", output.ToString());
        Assert.Contains("[SPIR-V] second", error.ToString());
    }
    #endregion

    #region Failure and cancellation
    /// <summary>A failed job cancels its active peer and prevents queued work from starting.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task FailureStopsAndJoinsOutstandingWork(bool throwException)
    {
        var peerStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        bool peerFinished = false, queuedStarted = false;
        var jobs = new[]
        {
            new ShaderCompilationJob("broken", async token =>
            {
                await peerStarted.Task.WaitAsync(token);
                if (throwException) throw new IOException("fixture failure");
                return new ShaderCompilerResult(7, "", "fixture compiler error");
            }),
            new ShaderCompilationJob("active", async token =>
            {
                peerStarted.SetResult();
                try { await Task.Delay(Timeout.Infinite, token); }
                finally { peerFinished = true; }
                return new ShaderCompilerResult(0, "", "");
            }),
            new ShaderCompilationJob("queued", token =>
            {
                queuedStarted = true;
                return Task.FromResult(new ShaderCompilerResult(0, "", ""));
            })
        };
        using var error = new StringWriter();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await Assert.ThrowsAsync<InvalidOperationException>(() => ShaderCompilationBatch.RunAsync(jobs, 2, TextWriter.Null, error, timeout.Token));
        Assert.True(peerFinished);
        Assert.False(queuedStarted);
        Assert.Contains("Failed broken", error.ToString());
        Assert.Contains(throwException ? "fixture failure" : "fixture compiler error", error.ToString());
    }

    /// <summary>Caller cancellation propagates only after active jobs finish their cleanup.</summary>
    [Fact]
    public async Task CallerCancellationWaitsForCleanup()
    {
        using var cancel = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        bool cleaned = false;
        var job = new ShaderCompilationJob("cancelled", async token =>
        {
            cancel.Cancel();
            try { await Task.Delay(Timeout.Infinite, token); }
            finally { cleaned = true; }
            return new ShaderCompilerResult(0, "", "");
        });
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            ShaderCompilationBatch.RunAsync([job], 1, TextWriter.Null, TextWriter.Null, cancel.Token));
        Assert.True(cleaned);
    }

    /// <summary>Invalid bounds and duplicate work fail before any compiler invocation.</summary>
    [Fact]
    public async Task RejectsInvalidBatchBeforeStarting()
    {
        bool started = false;
        var job = new ShaderCompilationJob("same", token =>
        {
            started = true;
            return Task.FromResult(new ShaderCompilerResult(0, "", ""));
        });
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => ShaderCompilationBatch.RunAsync([job], 0, TextWriter.Null, TextWriter.Null, TestContext.Current.CancellationToken));
        await Assert.ThrowsAsync<ArgumentException>(() => ShaderCompilationBatch.RunAsync([job, job], 2, TextWriter.Null, TextWriter.Null, TestContext.Current.CancellationToken));
        Assert.False(started);
    }
    #endregion
}
