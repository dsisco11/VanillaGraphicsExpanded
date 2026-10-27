using System;
using System.Diagnostics;
using System.Threading;
using VanillaGraphicsExpanded.Rendering.Contracts;
using VanillaGraphicsExpanded.Rendering.ProgramBinaries;
using VanillaGraphicsExpanded.Rendering.RHI;
using VanillaGraphicsExpanded.Rendering.Spirv;

namespace VanillaGraphicsExpanded.Rendering.ShaderCompilation;

/// <summary>Owns selected asset inputs and cache policy until a ready executable transfers to its asset owner.</summary>
internal sealed class ShaderAssetCandidate : IDisposable
{
    private readonly RHICommandContext context;
    private readonly ShaderLoadPlan plan;
    private RHIPendingShaderCreation? pending;
    private RHIShader? shader;
    private bool selectedBinary;
    private bool disposed;
    private bool transferred;

    #region Public API
    /// <summary>Retires abandoned candidates through their original context without disposing transferred results.</summary>
    public void Dispose()
    {
        if (disposed) return;
        // BeginCreateShader can already be ready on a synchronous driver; that result still belongs here.
        if (pending is { Status: CompletionStatus.Ready } ready) ready.Result.RequireResource().Dispose();
        pending?.Dispose();
        pending = null;
        shader?.Dispose();
        shader = null;
        disposed = true;
    }
    #endregion

    #region Internal API
    /// <summary>Gets captured and digest-validated assets used by both cached and fresh creation.</summary>
    internal PreparedProgramBinary Inputs { get; }
    /// <summary>Reports successful use of a persisted executable rather than fresh stage creation.</summary>
    internal bool Cached { get; private set; }
    /// <summary>Reports readiness without issuing native completion queries.</summary>
    internal bool IsReady => shader != null;

    /// <summary>Captures the execution context before asset callbacks and submits only immutable selected inputs.</summary>
    internal ShaderAssetCandidate(ShaderLoadPlan plan, ShaderAssetReader read, Func<ShaderBinaryDigest.Manifest?> digestIndex)
    {
        context = RHICommandContext.Current;
        this.plan = plan;
        Inputs = DriverProgramCache.Prepare(plan, read, digestIndex);
        try
        {
            var binary = DriverProgramCache.Select(Inputs);
            selectedBinary = binary != null;
            pending = context.BeginCreateShader(ShaderExecutableInputs.Create(plan, Inputs, binary));
        }
        catch { Dispose(); throw; }
    }

    /// <summary>Polls the original candidate and handles a detected cache rejection through explicit asset policy.</summary>
    internal bool TryComplete()
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        if (transferred) throw new InvalidOperationException("The shader candidate was already transferred.");
        if (shader != null) return true;
        var request = pending ?? throw new InvalidOperationException("The shader candidate has no pending request.");
        var status = request.TryComplete();
        if (status == CompletionStatus.Pending) return false;
        if (status == CompletionStatus.Failed && selectedBinary && request.Error == null)
        {
            // A detected binary rejection is a cache miss. Never reuse the rejected native allocation.
            request.Dispose();
            pending = null;
            selectedBinary = false;
            DriverProgramCache.Reject(Inputs);
            pending = context.BeginCreateShader(ShaderExecutableInputs.Create(plan, Inputs));
            return false;
        }
        if (status != CompletionStatus.Ready)
            throw new InvalidOperationException(request.Result.Diagnostics ?? "Shader asset creation did not complete.", request.Error);
        shader = request.Result.RequireResource();
        pending = null;
        request.Dispose();
        Cached = selectedBinary;
        DriverProgramCache.LastLoadWasHit = Cached;
        if (!Cached) DriverProgramCache.Save(context, shader, Inputs);
        return true;
    }

    /// <summary>Waits only at an asset dependency while retaining cancellation and bounded failure behavior.</summary>
    internal void Complete(CancellationToken cancellationToken = default)
    {
        long started = Stopwatch.GetTimestamp();
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (TryComplete()) return;
            if (Stopwatch.GetElapsedTime(started) > TimeSpan.FromSeconds(60))
                throw new TimeoutException($"Shader creation for {plan.Settings.Contract.Identity} did not complete within 60 seconds.");
            Thread.Yield();
        }
    }

    /// <summary>Transfers the ready owner once without exposing stages or adopting a native program name.</summary>
    internal RHIShader TakeShader()
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        if (shader == null) throw new InvalidOperationException("The shader candidate is unfinished or already transferred.");
        shader.RequireUsable();
        var result = shader;
        shader = null;
        transferred = true;
        return result;
    }
    #endregion


}
