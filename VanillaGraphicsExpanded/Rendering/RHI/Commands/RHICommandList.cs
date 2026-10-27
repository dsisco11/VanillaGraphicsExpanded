using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
namespace VanillaGraphicsExpanded.Rendering.RHI;
/// <summary>Owns one context-independent, single-use CPU recording for an explicit originating context.</summary>
public sealed partial class RHICommandList : IDisposable
{
    private readonly RHICommandContext origin;
    private int state;
    private bool disposed, inPass, conditional, capture, paused;
    private int pipelineKind;
    private BoundaryOperations operations;
    private readonly HashSet<RHIGraphicsPipelineRef> graphics = new();
    private readonly HashSet<RHIComputePipelineRef> compute = new();
    private readonly HashSet<GpuQueryRef> queries = new();
    private readonly HashSet<int> uniformSlots = new();
    private readonly HashSet<(int Slot, TextureDimension Dimension)> sampledSlots = new();
    private readonly List<IRHIRecordedResult> requests = new();
    private RHIBoundaryDescription? boundary;
    #region Public API
    /// <summary>Gets the published CPU lifecycle; it does not report GPU completion.</summary>
    public CommandListState State
    {
        get
        {
            var current = (CommandListState)Volatile.Read(ref state);
            // Recording itself never consults lifetime knowledge. Closed-work observation may abandon a retired origin.
            if (current == CommandListState.Closed && origin.HasRetiredOrigin)
            {
                origin.RequireRecordingThread(); Cancel(); return CommandListState.Cancelled;
            }
            return current;
        }
    }
    /// <summary>Gets the count of complete command headers.</summary>
    public int Count => headers.Count;
    /// <summary>Gets the frozen footprint after closing; enter it explicitly when restoration is required.</summary>
    public RHIBoundaryDescription Boundary => boundary ?? throw new InvalidOperationException("Close the list before inspecting its declaration.");
    /// <summary>Creates empty list-local storage without querying the context or resources.</summary>
    public RHICommandList(RHICommandContext context)
    { origin = context ?? throw new ArgumentNullException(nameof(context)); origin.RequireRecordingThread(); }
    /// <summary>Publishes complete arguments and a CPU-derived footprint, rejecting intervals that escape the list.</summary>
    public void Close(bool conditionalRenderingInactive = false)
    {
        RequireRecording();
        if (inPass || capture || conditional || queries.Count != 0)
            throw new InvalidOperationException("End every pass, query, capture and condition before closing.");
        boundary = new("Recorded RHI commands", conditionalRenderingInactive, graphics, compute, operations,
            sampledSlots.Select(slot => new RHISampledTextureSlot(slot.Slot, default, Dimension: slot.Dimension)), uniformSlots);
        Volatile.Write(ref state, (int)CommandListState.Closed);
    }
    /// <summary>Cancels only unexecuted work without allocating staging or consuming uniform versions.</summary>
    public void Cancel()
    {
        origin.RequireRecordingThread();
        if ((CommandListState)Volatile.Read(ref state) is not (CommandListState.Recording or CommandListState.Closed))
            throw new InvalidOperationException("Only unexecuted recordings can be cancelled.");
        AbandonRequests(null); ClearStorage();
        Volatile.Write(ref state, (int)CommandListState.Cancelled);
    }
    /// <summary>Reuses CPU pages only after a terminal recording; issued tickets remain independently owned.</summary>
    public void Reset()
    {
        origin.RequireRecordingThread(); ObjectDisposedException.ThrowIf(disposed, this);
        if (State is not (CommandListState.Executed or CommandListState.Failed or CommandListState.Cancelled))
            throw new InvalidOperationException("Reset requires a terminal recording.");
        ClearStorage();
        Volatile.Write(ref state, (int)CommandListState.Recording);
    }
    /// <summary>Releases CPU retention only; already-enqueued staging and fences belong to their tickets.</summary>
    public void Dispose()
    {
        origin.RequireRecordingThread();
        if (disposed) return;
        if (State == CommandListState.Executing) throw new InvalidOperationException("Cannot dispose an executing list.");
        if (State is CommandListState.Recording or CommandListState.Closed) Cancel();
        ClearStorage();
        disposed = true;
    }
    #endregion
    #region Internal API
    /// <summary>Admits one synchronous submission without native work or accepting a different context instance.</summary>
    internal void BeginExecution(RHICommandContext context)
    {
        origin.RequireRecordingThread(); ObjectDisposedException.ThrowIf(disposed, this);
        if (!ReferenceEquals(origin, context)) throw new InvalidOperationException("Submit on the list's original context.");
        if (Interlocked.CompareExchange(ref state, (int)CommandListState.Executing, (int)CommandListState.Closed) != (int)CommandListState.Closed)
            throw new InvalidOperationException("Only a closed, unexecuted list can be submitted.");
    }
    /// <summary>Publishes terminal dispatch and completes only requests that never reached the backend.</summary>
    internal void CompleteExecution(CommandListState terminal, Exception? error)
    { AbandonRequests(error); Volatile.Write(ref state, (int)terminal); }
    #endregion
    #region Private
    /// <summary>Rejects foreign-thread or post-close edits before retaining any argument.</summary>
    private void RequireRecording()
    {
        origin.RequireRecordingThread(); ObjectDisposedException.ThrowIf(disposed, this);
        if (State != CommandListState.Recording) throw new InvalidOperationException("The list is not recording.");
    }
    /// <summary>Withdraws only CPU promises, preserving independently issued completion ownership.</summary>
    private void AbandonRequests(Exception? error) { foreach (var request in requests) request.Abandon(error); }
    #endregion
}
