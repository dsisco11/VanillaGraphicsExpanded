using System;
using System.Collections.Generic;
using System.Linq;
namespace VanillaGraphicsExpanded.Rendering.RHI;
/// <summary>Submits ordered CPU recordings through the same immediate operations and ownership services.</summary>
public sealed partial class RHICommandContext
{
    private readonly int recordingThread = Environment.CurrentManagedThreadId;
    private bool executingCommands;
    internal bool recordedConditional, recordedCapture, recordedPass;
    private List<GpuQueryRef>? recordedQueries;
    #region Public API
    /// <summary>Creates list-local CPU storage for this explicit context without requiring a current native context.</summary>
    public RHICommandList CreateCommandList() => new(this);
    /// <summary>Dispatches once in caller order, preserving the first error and independently owned GPU requests.</summary>
    public RHISubmissionResult Submit(RHICommandList list)
    {
        ArgumentNullException.ThrowIfNull(list);
        RequireRecordingThread(); RequireIdleSubmission();
        if (executingCommands) throw new InvalidOperationException("Command list submission cannot nest.");
        list.BeginExecution(this);
        if (backend.HasRetiredOrigin)
        { list.CompleteExecution(CommandListState.Cancelled, null); return new(CommandListState.Cancelled); }
        int index = 0;
        executingCommands = true;
        try
        {
            backend.RequireContext();
            if (backend.HasRenderPass) throw new InvalidOperationException("A recorded list cannot enter or leave an existing pass.");
            RequireAdmittedList(list.Boundary);
            for (; index < list.Count; index++)
            {
                backend.RequireContext();
                list.Execute(index, this);
            }
            list.CompleteExecution(CommandListState.Executed, null);
            return new(CommandListState.Executed);
        }
        catch (Exception error)
        {
            Exception? cleanup = UnwindRecordedScopes();
            var terminal = backend.HasRetiredOrigin ? CommandListState.Cancelled : CommandListState.Failed;
            list.CompleteExecution(terminal, terminal == CommandListState.Cancelled ? null : error);
            return new(terminal, index, error, cleanup);
        }
        finally { executingCommands = false; }
    }
    #endregion
    #region Internal API
    /// <summary>Reports original-context retirement without native discovery.</summary>
    internal bool HasRetiredOrigin => backend.HasRetiredOrigin;
    /// <summary>Enforces exclusive originating-thread CPU ownership without requiring a current graphics context.</summary>
    internal void RequireRecordingThread()
    {
        if (Environment.CurrentManagedThreadId != recordingThread) throw new InvalidOperationException("The originating recording thread is required.");
    }
    /// <summary>Retains only successfully begun query intervals for interruption cleanup.</summary>
    internal void RecordedQueryStarted(GpuQueryRef query) => (recordedQueries ??= new()).Add(query);
    /// <summary>Withdraws an ended query without inspecting its resource metadata.</summary>
    internal void RecordedQueryEnded(GpuQueryRef query) => recordedQueries!.Remove(query);
    #endregion
    #region Private
    /// <summary>Rejects undeclared effects before the first list command, without silently widening restoration coverage.</summary>
    private void RequireAdmittedList(RHIBoundaryDescription footprint)
    {
        if (activeBoundary is null)
        {
            if (footprint.Operations.HasFlag(BoundaryOperations.RenderPasses))
                throw new InvalidOperationException("Enter the closed list's boundary before executing passes or image blits.");
            return;
        }
        var admitted = activeBoundary.Description;
        if ((footprint.Operations & ~admitted.Operations) != 0
            || footprint.Graphics.Any(pipeline => !admitted.Graphics.Contains(pipeline))
            || footprint.Compute.Any(pipeline => !admitted.Compute.Contains(pipeline))
            || footprint.SampledSlots.Any(slot => !admitted.SampledSlots.Contains(slot))
            || footprint.UniformSlots.Any(slot => !admitted.UniformSlots.Contains(slot)))
            throw new InvalidOperationException("The existing boundary does not admit the closed list's complete footprint.");
    }
    /// <summary>Unwinds only this list's active intervals, preserving the original error independently of cleanup errors.</summary>
    private Exception? UnwindRecordedScopes()
    {
        List<Exception>? failures = null;
        // Every cleanup service owns original-context admission and CPU abandonment on retirement.
        if (recordedCapture)
        {
            try { backend.EndTransformFeedback(); } catch (Exception error) { (failures ??= new()).Add(error); }
            finally { recordedCapture = false; }
        }
        if (recordedConditional)
        {
            try { backend.EndConditionalRender(); } catch (Exception error) { (failures ??= new()).Add(error); }
            finally { recordedConditional = false; }
        }
        if (recordedQueries is not null)
        {
            foreach (var query in recordedQueries)
                try { backend.EndQuery(query); } catch (Exception error) { (failures ??= new()).Add(error); }
            recordedQueries.Clear();
        }
        if (recordedPass)
        {
            try { if (backend.HasRenderPass) backend.EndRenderPass(); } catch (Exception error) { (failures ??= new()).Add(error); }
            finally { recordedPass = false; selectedGraphicsPipeline = default; selectedResources = null; }
        }
        return failures is null ? null : new AggregateException(failures);
    }
    #endregion
}
