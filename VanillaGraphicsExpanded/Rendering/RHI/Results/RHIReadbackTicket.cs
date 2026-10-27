using System;
using VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;

namespace VanillaGraphicsExpanded.Rendering.RHI;

/// <summary>Owns staging and its completion independently of any command-list or source lifetime.</summary>
public sealed class RHIReadbackTicket : IDisposable
{
    private readonly OpenGLBackend backend;
    private GpuBufferObject? staging;
    private GpuFence? fence;
    private bool mapped;
    #region Public API
    /// <summary>Gets the maximum synchronous readback wait; completion returns immediately when ready.</summary>
    public static TimeSpan DefaultWaitTimeout { get; } = TimeSpan.FromSeconds(60);
    /// <summary>Gets completion state without implicitly polling or waiting.</summary>
    public CompletionStatus Status { get; private set; } = CompletionStatus.Pending;
    /// <summary>Gets the original dispatch or completion error.</summary>
    public Exception? Error { get; private set; }
    /// <summary>Gets the immutable byte extent of this request.</summary>
    public int ByteCount { get; }
    /// <summary>Polls only this request's explicit fence.</summary>
    public CompletionStatus Poll() => Observe(TimeSpan.Zero, false);
    /// <summary>Waits synchronously using the shared readback timeout policy.</summary>
    public CompletionStatus Wait() => Observe(DefaultWaitTimeout, true);
    /// <summary>Explicitly waits at most the supplied duration.</summary>
    public CompletionStatus Wait(TimeSpan timeout) => Observe(timeout, true);
    /// <summary>Maps ready storage exclusively; no implicit wait or source rediscovery occurs.</summary>
    public RHIMappedReadback MapReadyReadback()
    {
        if (backend.HasRetiredOrigin)
        {
            Status = CompletionStatus.Cancelled;
            ReleaseStorage();
            throw new InvalidOperationException("The readback context has retired.");
        }
        backend.RequireContext();
        if (Status != CompletionStatus.Ready || staging is null || mapped)
            throw new InvalidOperationException("Readback must be ready and not already mapped.");
        try
        {
            nint address = backend.MapBuffer(staging, 0, ByteCount, BufferMapAccess.Read);
            if (address == 0) throw new InvalidOperationException("Readback mapping failed.");
            mapped = true;
            return new(this, address, ByteCount);
        }
        catch (Exception error)
        {
            if (mapped)
            {
                try { if (backend.IsCurrentContext) backend.UnmapBuffer(staging); } catch { }
                mapped = false;
            }
            Error = error; Status = CompletionStatus.Failed; throw;
        }
    }
    /// <summary>Cancels CPU observation and retires owned staging; native deferred destruction protects in-flight copies.</summary>
    public void Cancel()
    {
        if (mapped) throw new InvalidOperationException("Release the active readback mapping before cancellation.");
        if (Status == CompletionStatus.Pending) Status = CompletionStatus.Cancelled;
        ReleaseStorage();
    }
    /// <summary>Releases private completion/storage ownership without recycling an in-flight staging interval.</summary>
    public void Dispose() => Cancel();
    #endregion
    #region Internal API
    /// <summary>Reports whether an exclusive mapping still requires scope-owned cleanup.</summary>
    internal bool HasActiveMapping => mapped;
    /// <summary>Takes exclusive ownership of already-enqueued staging and its fence.</summary>
    internal RHIReadbackTicket(OpenGLBackend backend, GpuBufferObject staging, GpuFence fence, int byteCount)
    { this.backend = backend; this.staging = staging; this.fence = fence; ByteCount = byteCount; }
    /// <summary>Rejects mapped access in a foreign context or after the scope ends.</summary>
    internal void RequireMapping()
    {
        backend.RequireContext();
        if (!mapped || staging is null || Status != CompletionStatus.Ready) throw new InvalidOperationException("The mapping has ended.");
    }
    /// <summary>Ends the exclusive mapping without relinquishing staging until native unmap finishes.</summary>
    internal void Unmap()
    {
        if (backend.HasRetiredOrigin)
        {
            // A dead namespace cannot receive an unmap in a later native context.
            mapped = false;
            Status = CompletionStatus.Cancelled;
            ReleaseStorage();
            return;
        }
        RequireMapping();
        try
        {
            if (!backend.UnmapBuffer(staging!))
                throw new InvalidOperationException("Readback storage became corrupt.");
        }
        catch (Exception error) { Status = CompletionStatus.Failed; Error = error; throw; }
        finally { mapped = false; }
    }
    #endregion
    #region Private
    /// <summary>Checks actual fence completion; failures do not authorize another mapping.</summary>
    private CompletionStatus Observe(TimeSpan timeout, bool flush)
    {
        if (Status != CompletionStatus.Pending) return Status;
        if (backend.HasRetiredOrigin) { Cancel(); return Status; }
        backend.RequireContext();
        try
        {
            Status = backend.WaitCpu(fence!, timeout, flush);
            if (Status == CompletionStatus.Failed) Error = new InvalidOperationException("Readback completion failed.");
        }
        catch (Exception error) { Error = error; Status = CompletionStatus.Failed; }
        return Status;
    }
    /// <summary>Retires each original owner exactly once, preserving ownership when local-context disposal rejects.</summary>
    private void ReleaseStorage()
    {
        fence?.Dispose(); fence = null;
        staging?.Dispose(); staging = null;
    }
    #endregion
}
