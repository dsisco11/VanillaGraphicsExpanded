using System;
using VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;

namespace VanillaGraphicsExpanded.Rendering.RHI;

/// <summary>Observes one issued query generation without taking ownership of its resource.</summary>
public sealed class RHIQueryTicket
{
    private readonly OpenGLBackend backend;
    private readonly GpuQuery query;
    private readonly long revision;
    private ulong result;
    #region Public API
    /// <summary>Reports the request state; CPU dispatch alone never makes it ready.</summary>
    public RHICompletionStatus Status { get; private set; } = RHICompletionStatus.Pending;
    /// <summary>Provides the original result failure when observation fails.</summary>
    public Exception? Error { get; private set; }
    /// <summary>Returns a value only after successful result completion.</summary>
    public ulong Result => Status == RHICompletionStatus.Ready ? result : throw new InvalidOperationException("The query result is not ready.");
    /// <summary>Polls without blocking, rejecting retired or superseded requests.</summary>
    public RHICompletionStatus Poll() => Observe(false);
    /// <summary>Explicitly waits for the query result on the originating context.</summary>
    public RHICompletionStatus Wait() => Observe(true);
    /// <summary>Cancels CPU observation without deleting the externally owned query.</summary>
    public void Cancel() { if (Status == RHICompletionStatus.Pending) Status = RHICompletionStatus.Cancelled; }
    #endregion
    #region Internal API
    /// <summary>Captures backend, resource and issued revision without a native read.</summary>
    internal RHIQueryTicket(OpenGLBackend backend, GpuQuery query, long revision)
    { this.backend = backend; this.query = query; this.revision = revision; }
    #endregion
    #region Private
    /// <summary>Publishes ready only after the backend supplies the completed captured result.</summary>
    private RHICompletionStatus Observe(bool wait)
    {
        if (Status != RHICompletionStatus.Pending) return Status;
        if (backend.HasRetiredOrigin) return Status = RHICompletionStatus.Cancelled;
        backend.RequireContext();
        try { if (backend.TryReadQuery(query, revision, wait, out result)) Status = RHICompletionStatus.Ready; }
        catch (Exception error) { Error = error; Status = RHICompletionStatus.Failed; }
        return Status;
    }
    #endregion
}
