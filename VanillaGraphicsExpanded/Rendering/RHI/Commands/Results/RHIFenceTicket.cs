using System;
namespace VanillaGraphicsExpanded.Rendering.RHI;
/// <summary>Owns an explicit recorded fence after dispatch; pending recording grants no native sync reference.</summary>
public sealed class RHIFenceTicket : IDisposable, IRHIRecordedResult
{
    private RHICommandContext? context;
    private readonly RHICommandContext pendingOrigin;
    private GpuFence? fence;
    #region Public API
    /// <summary>Gets completion knowledge without implicit polling.</summary>
    public CompletionStatus Status { get; private set; } = CompletionStatus.Pending;
    /// <summary>Gets the original dispatch or observation error.</summary>
    public Exception? Error { get; private set; }
    /// <summary>Polls the explicit marker without flushing or waiting.</summary>
    public CompletionStatus Poll() => Observe(TimeSpan.Zero, false);
    /// <summary>Explicitly waits only after the marker has been submitted.</summary>
    public CompletionStatus Wait(TimeSpan timeout) => Observe(timeout, true);
    /// <summary>Ends observation and retires only this ticket's original fence owner.</summary>
    public void Dispose()
    {
        if (Status == CompletionStatus.Pending) Status = CompletionStatus.Cancelled;
        fence?.Dispose(); fence = null;
    }
    #endregion
    #region Internal API
    /// <summary>Creates a CPU-only result marker without native work.</summary>
    internal RHIFenceTicket(RHICommandContext origin) => pendingOrigin = origin;
    /// <summary>Transfers the already-inserted fence to completion ownership independent of list reset.</summary>
    internal void Adopt(RHICommandContext origin, GpuFence issued) { context = origin; fence = issued; }
    /// <inheritdoc />
    void IRHIRecordedResult.Abandon(Exception? error)
    {
        if (context is not null || Status != CompletionStatus.Pending) return;
        Error = error; Status = error is null ? CompletionStatus.Cancelled : CompletionStatus.Failed;
    }
    #endregion
    #region Private
    /// <summary>Checks only the original context's explicit fence, never an ambient replacement.</summary>
    private CompletionStatus Observe(TimeSpan timeout, bool flush)
    {
        if (Status != CompletionStatus.Pending) return Status;
        if (context is null)
        {
            if (pendingOrigin.HasRetiredOrigin) Status = CompletionStatus.Cancelled;
            return Status;
        }
        if (context.HasRetiredOrigin) { Status = CompletionStatus.Cancelled; return Status; }
        context.RequireContext();
        try
        {
            Status = context.WaitCpu(fence!, timeout, flush);
            if (Status == CompletionStatus.Failed) Error = new InvalidOperationException("Fence completion failed.");
        }
        catch (Exception error) { Error = error; Status = CompletionStatus.Failed; }
        return Status;
    }
    #endregion
}
