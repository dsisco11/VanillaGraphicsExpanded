using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.Rendering.Contracts;
using VanillaGraphicsExpanded.Rendering.ProgramBinaries;
using Vintagestory.API.Common;

namespace VanillaGraphicsExpanded.Rendering.ShaderCompilation;

/// <summary>Bounds render-thread submissions and transfers completed candidates to their existing program owners.</summary>
internal sealed class ShaderLinkBatch : IDisposable
{
    [ThreadStatic] private static ShaderLinkBatch? current;
    [ThreadStatic] private static bool forceSynchronous;
    [ThreadStatic] private static Action<ShaderLinkBatch>? observation;
    private readonly ShaderLinkBatch? previous;
    private readonly IAssetManager assets;
    private readonly string domain;
    private readonly CancellationToken cancellation;
    private readonly GetProgramParameterName completionQuery;
    private readonly Queue<ShaderLoadPlan> waiting;
    private readonly List<(ShaderLoadPlan Plan, PendingShaderProgram? Program, Exception? Error)> submitted = new();
    private readonly int limit;
    private readonly int thread = Environment.CurrentManagedThreadId;
    private bool disposed;
    internal bool Enabled { get; }
    internal int Outstanding => submitted.Count;
    internal int CompletedOutstanding => submitted.Count(entry => entry.Program?.Validated == true);
    internal int PeakOutstanding { get; private set; }
    internal double SubmissionMilliseconds { get; private set; }
    internal double CompletionMilliseconds { get; private set; }
    internal int ConsumedCount { get; private set; }

    #region Submission and consumption
    /// <summary>Submits a bounded window of exact settings; unsupported contexts retain ordinary synchronous loading.</summary>
    internal ShaderLinkBatch(IAssetManager assets, string domain, IEnumerable<ShaderSettings> settings,
        int maximumOutstanding = 8, CancellationToken cancellation = default, bool disable = false)
    {
        if (maximumOutstanding < 1) throw new ArgumentOutOfRangeException(nameof(maximumOutstanding));
        cancellation.ThrowIfCancellationRequested();
        this.assets = assets;
        this.domain = domain;
        this.cancellation = cancellation;
        limit = maximumOutstanding;
        waiting = new Queue<ShaderLoadPlan>(settings.Select(value => new ShaderLoadPlan(value)));
        previous = current;
        // Nested creation must not add a second submission window over unfinished outer work.
        previous?.WaitForSubmitted();
        disable |= forceSynchronous;
        bool arb = !disable && GlExtensions.Supports("GL_ARB_parallel_shader_compile");
        Enabled = arb || (!disable && GlExtensions.Supports("GL_KHR_parallel_shader_compile"));
        completionQuery = arb ? (GetProgramParameterName)ArbParallelShaderCompile.CompletionStatusArb
            : (GetProgramParameterName)KhrParallelShaderCompile.CompletionStatusKhr;
        current = this;
        try { if (Enabled) Fill(); }
        catch { Dispose(); throw; }
    }

    /// <summary>Consumes only an exact plan from the same asset owner and domain, without publishing its handle.</summary>
    internal static PendingShaderProgram? Take(IAssetManager assets, string domain, ShaderLoadPlan plan)
    {
        var batch = current;
        if (batch == null) return null;
        batch.CheckThread();
        batch.cancellation.ThrowIfCancellationRequested();
        if (!batch.Enabled) return null;
        if (!ReferenceEquals(batch.assets, assets) || batch.domain != domain)
        {
            batch.WaitForSubmitted();
            return null;
        }
        int index = batch.submitted.FindIndex(item => item.Plan.SameInputs(plan));
        if (index < 0)
        {
            // An unexpected dependency or a newer settings snapshot uses the ordinary loader only
            // after the current window finishes. This preserves the bound even outside planned order.
            batch.WaitForSubmitted();
            var remaining = batch.waiting.Where(item => !item.SameInputs(plan)).ToArray();
            batch.waiting.Clear();
            foreach (var item in remaining) batch.waiting.Enqueue(item);
            return null;
        }
        var entry = batch.submitted[index];
        batch.submitted.RemoveAt(index);
        try
        {
            if (entry.Error != null)
                throw new InvalidOperationException($"Shader submission failed for {plan.Settings.Contract.Identity}: {entry.Error.Message}", entry.Error);
            long started = Stopwatch.GetTimestamp();
            entry.Program!.Complete(batch.cancellation);
            batch.CompletionMilliseconds += Stopwatch.GetElapsedTime(started).TotalMilliseconds;
            batch.ConsumedCount++;
            // Refill only after this dependency completes so driver work never exceeds the window.
            batch.Fill();
            return entry.Program;
        }
        catch { entry.Program?.Dispose(); throw; }
    }

    /// <summary>Establishes completion before an unplanned synchronous dependency or nested batch can submit work.</summary>
    private void WaitForSubmitted()
    {
        CheckThread();
        cancellation.ThrowIfCancellationRequested();
        for (int index = 0; index < submitted.Count; index++)
        {
            var entry = submitted[index];
            if (entry.Program == null) continue;
            long started = Stopwatch.GetTimestamp();
            try { entry.Program.Complete(cancellation); }
            catch (OperationCanceledException) { throw; }
            catch (Exception error)
            {
                entry.Program.Dispose();
                submitted[index] = (entry.Plan, null, error);
            }
            finally { CompletionMilliseconds += Stopwatch.GetElapsedTime(started).TotalMilliseconds; }
        }
    }

    /// <summary>Reads captured assets and submits independent work while recording failures for the requesting owner.</summary>
    private void Fill()
    {
        ReadOnlySpan<byte> Read(string path) => assets.TryGet(AssetLocation.Create("shaders/" + path, domain), loadAsset: true)?.Data
            ?? throw new InvalidOperationException("Missing built shader asset: " + domain + ":shaders/" + path);
        while (submitted.Count < limit && waiting.TryDequeue(out var plan))
        {
            cancellation.ThrowIfCancellationRequested();
            try
            {
                long started = Stopwatch.GetTimestamp();
                var pending = new PendingShaderProgram(plan, Read, () => ShaderDigestIndexCache.ForAssets(assets, domain), completionQuery);
                SubmissionMilliseconds += Stopwatch.GetElapsedTime(started).TotalMilliseconds;
                submitted.Add((plan, pending, null));
            }
            catch (Exception error) { submitted.Add((plan, null, error)); }
            PeakOutstanding = Math.Max(PeakOutstanding, submitted.Count);
        }
    }
    #endregion

    #region Lifetime
    /// <summary>Rejects cross-thread operations before issuing any GL calls.</summary>
    private void CheckThread()
    {
        if (Environment.CurrentManagedThreadId != thread) throw new InvalidOperationException("Shader batches belong to their render thread.");
    }

    /// <summary>Deletes abandoned candidates and restores the enclosing batch on the owning context.</summary>
    public void Dispose()
    {
        CheckThread();
        if (disposed) return;
        if (!ReferenceEquals(current, this)) throw new InvalidOperationException("Shader batch scopes must be disposed in reverse order.");
        disposed = true;
        current = previous;
        foreach (var entry in submitted) entry.Program?.Dispose();
        submitted.Clear();
        waiting.Clear();
        observation?.Invoke(this);
    }
    #endregion

    #region Test isolation
    /// <summary>Exercises the production fallback path without changing driver capability or other GL threads.</summary>
    internal static IDisposable UseSynchronousForTesting() => new SynchronousOverride();

    /// <summary>Records completed production scopes without installing a permanent telemetry consumer.</summary>
    internal static IDisposable ObserveForTesting(Action<ShaderLinkBatch> observer) => new ObservationOverride(observer);

    /// <summary>Owns one thread-local measurement callback for a focused production workload.</summary>
    private sealed class ObservationOverride : IDisposable
    {
        private readonly Action<ShaderLinkBatch>? previous = observation;
        /// <summary>Captures completed-scope timings until the measurement ends.</summary>
        internal ObservationOverride(Action<ShaderLinkBatch> observer) => observation = observer;
        /// <summary>Restores the prior observation callback.</summary>
        public void Dispose() => observation = previous;
    }

    /// <summary>Restores the calling thread's previous test loading policy.</summary>
    private sealed class SynchronousOverride : IDisposable
    {
        private readonly bool previous = forceSynchronous;
        /// <summary>Disables overlapping submissions for a matched production workload.</summary>
        internal SynchronousOverride() => forceSynchronous = true;
        /// <summary>Restores the policy after the workload finishes.</summary>
        public void Dispose() => forceSynchronous = previous;
    }
    #endregion
}
