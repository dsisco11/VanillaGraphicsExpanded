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
    public CompletionStatus Status { get; private set; } = CompletionStatus.Pending;
    /// <summary>Provides the original result failure when observation fails.</summary>
    public Exception? Error { get; private set; }
    /// <summary>Returns a value only after successful result completion.</summary>
    public ulong Result => Status == CompletionStatus.Ready ? result : throw new InvalidOperationException("The query result is not ready.");
    /// <summary>Polls without blocking, rejecting retired or superseded requests.</summary>
    public CompletionStatus Poll() => Observe(false);
    /// <summary>Explicitly waits for the query result on the originating context.</summary>
    public CompletionStatus Wait() => Observe(true);
    /// <summary>Cancels CPU observation without deleting the externally owned query.</summary>
    public void Cancel() { if (Status == CompletionStatus.Pending) Status = CompletionStatus.Cancelled; }
    #endregion
    #region Internal API
    /// <summary>Captures backend, resource and issued revision without a native read.</summary>
    internal RHIQueryTicket(OpenGLBackend backend, GpuQuery query, long revision)
    { this.backend = backend; this.query = query; this.revision = revision; }
    #endregion
    #region Private
    /// <summary>Publishes ready only after the backend supplies the completed captured result.</summary>
    private CompletionStatus Observe(bool wait)
    {
        if (Status != CompletionStatus.Pending) return Status;
        if (backend.HasRetiredOrigin) return Status = CompletionStatus.Cancelled;
        backend.RequireContext();
        try { if (backend.TryReadQuery(query, revision, wait, out result)) Status = CompletionStatus.Ready; }
        catch (Exception error) { Error = error; Status = CompletionStatus.Failed; }
        return Status;
    }
    #endregion
}
