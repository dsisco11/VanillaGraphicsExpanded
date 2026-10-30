using System.Collections.Generic;
using System.Runtime.CompilerServices;
using OpenTK.Graphics.OpenGL4;

namespace VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;

/// <summary>Owns target and activity facts needed to distinguish legal query realization from rejection.</summary>
internal sealed partial class OpenGlStateCache
{
    private readonly ConditionalWeakTable<GpuResourceRegistration, QueryExecutionKnowledge> queryTargets = new();
    private readonly Dictionary<(int Target, uint Index), (GpuResourceRegistration Entry, int Target, ulong Revision)> observedActiveQueries = new();
    private GpuResourceRegistration? observedConditionalQuery;
    private ulong observedConditionRevision;

    #region Internal API
    #region Query type and intervals
    /// <summary>Records immutable native type without putting a descriptor mirror in the empty canonical schema.</summary>
    internal void EstablishQueryTarget(int id, int target)
    {
        if (RHIResourceManagerSystem.CurrentManager?.FindObservedResource(GpuResourceKind.Query, id) is { } entry)
            queryTargets.TryAdd(entry, new(NormalizeQueryTarget(target)));
    }

    /// <summary>Realizes only the original generated name after target, index and activity checks.</summary>
    internal void ObserveQueryBegin(int target, uint index, int id)
    {
        var manager = RHIResourceManagerSystem.CurrentManager;
        if (manager?.FindObservedResource(GpuResourceKind.Query, id) is not { } entry) return;
        if (index != 0 && target != (int)QueryTarget.PrimitivesGenerated
            && target != (int)QueryTarget.TransformFeedbackPrimitivesWritten) return;
        int type = NormalizeQueryTarget(target);
        var key = (QueryActivityTarget(target), index);
        if (observedActiveQueries.ContainsKey(key) || HasActiveQuery(entry)
            || ReferenceEquals(observedConditionalQuery, entry)) return;
        if (queryTargets.TryGetValue(entry, out var previous) && previous.Target != type) return;
        if (!IsSupportedQueryTarget(target, false) || !IsQueryIndexValid(target, index))
        {
            // Missing cached limits differ from proven rejection. Do not let a later bind
            // fabricate defaults after this name may already have acquired a different type.
            if (!GpuSupport.HasCachedCapabilitiesForCurrentContext && (IsStatisticsQueryTarget(target)
                || index > 0 && (target == (int)QueryTarget.PrimitivesGenerated
                    || target == (int)QueryTarget.TransformFeedbackPrimitivesWritten)))
            {
                // Acceptance is unresolved, so this interval may occupy its slot until an
                // observed end. Otherwise a later rejected begin could manufacture realization.
                if (previous is not null) previous.Revision++;
                observedActiveQueries[key] = (entry, target, previous?.Revision ?? 0);
                if (entry is GpuResourceMetadataEntry<GpuQueryMetadata> { IsRealized: false } pending)
                    pending.Withdraw(GpuMetadataChange.Metadata);
            }
            return;
        }
        if (!entry.IsRealized && entry is GpuResourceMetadataEntry<GpuQueryMetadata> { PendingChange: not GpuMetadataChange.None })
        {
            observedActiveQueries[key] = (entry, target, 0);
            return;
        }
        // A realized pre-install object with unknown type cannot be retyped by observation.
        if (entry.IsRealized && previous is null)
        {
            observedActiveQueries[key] = (entry, target, 0);
            return;
        }
        manager.RealizeObservedQuery(id);
        if (previous is null) queryTargets.Add(entry, previous = new(type));
        previous.Revision++;
        observedActiveQueries[key] = (entry, target, previous.Revision);
    }

    /// <summary>Releases the native interval without changing identity or descriptor revisions.</summary>
    internal void ObserveQueryEnd(int target, uint index)
    {
        var key = (QueryActivityTarget(target), index);
        // An observed interval already supplies its target/index acceptance facts. Ending
        // it does not require capabilities to remain cached for a second classification.
        if (observedActiveQueries.TryGetValue(key, out var active) && active.Target == target)
            observedActiveQueries.Remove(key);
    }

    /// <summary>Realizes a timestamp reservation without polling the resulting timestamp.</summary>
    internal void ObserveQueryTimestamp(int id, int target)
    {
        if (target != (int)QueryTarget.Timestamp) return;
        var manager = RHIResourceManagerSystem.CurrentManager;
        if (manager?.FindObservedResource(GpuResourceKind.Query, id) is not { } entry) return;
        if (!entry.IsRealized && entry is GpuResourceMetadataEntry<GpuQueryMetadata> { PendingChange: not GpuMetadataChange.None }) return;
        if (queryTargets.TryGetValue(entry, out var previous) && previous.Target != target) return;
        if (entry.IsRealized && previous is null) return;
        manager.RealizeObservedQuery(id);
        if (previous is null) queryTargets.Add(entry, previous = new(target));
        previous.Revision++;
    }

    /// <summary>Retains the current conditional consumer solely for query acceptance classification.</summary>
    internal void ObserveQueryCondition(int id, int mode)
    {
        if (observedConditionalQuery is not null || !System.Enum.IsDefined((ConditionalRenderType)mode)) return;
        if (RHIResourceManagerSystem.CurrentManager?.FindObservedResource(GpuResourceKind.Query, id) is not { IsRealized: true } entry
            || HasActiveQuery(entry)) return;
        if (queryTargets.TryGetValue(entry, out var state) && state.Target != (int)QueryTarget.SamplesPassed
            && state.Target != (int)QueryTarget.AnySamplesPassed) return;
        observedConditionalQuery = entry;
        observedConditionRevision++;
    }

    /// <summary>Ends conditional consumption without scanning native query state.</summary>
    internal void EndQueryCondition() => observedConditionalQuery = null;

    /// <summary>Reports an established or possibly active external condition without acquiring native state.</summary>
    internal bool HasQueryCondition => observedConditionalQuery is not null;
    /// <summary>Identifies a particular conditional interval even when the same query is begun again.</summary>
    internal ulong QueryConditionRevision => observedConditionRevision;

    /// <summary>Prevents a managed end from terminating a replacement external condition.</summary>
    internal bool IsQueryConditionCurrent(GpuQuery owner, ulong revision)
    {
        var entry = owner.ResourceManager?.GetResourceRegistration(owner);
        return revision == observedConditionRevision
            && (entry is null ? observedConditionalQuery is null : ReferenceEquals(entry, observedConditionalQuery));
    }

    /// <summary>Checks the original active interval instead of ending a later external reissue.</summary>
    internal bool IsQueryIntervalCurrent(GpuQuery owner, int target, ulong revision)
    {
        var entry = owner.ResourceManager?.GetResourceRegistration(owner);
        bool active = observedActiveQueries.TryGetValue((QueryActivityTarget(target), 0), out var interval);
        return entry is null ? !active : active && ReferenceEquals(entry, interval.Entry) && interval.Revision == revision;
    }

    /// <summary>Rejects managed query starts that conflict with an observed external or managed interval.</summary>
    internal bool HasQueryStartConflict(int target, int id)
    {
        if (observedActiveQueries.ContainsKey((QueryActivityTarget(target), 0))) return true;
        var entry = RHIResourceManagerSystem.CurrentManager?.FindObservedResource(GpuResourceKind.Query, id);
        return entry is not null && (HasActiveQuery(entry) || ReferenceEquals(observedConditionalQuery, entry));
    }
    #endregion

    #region Result generations and retirement
    /// <summary>Certifies the result generation without making content changes canonical descriptor revisions.</summary>
    internal ulong QueryExecutionRevision(int id)
    {
        return RHIResourceManagerSystem.CurrentManager?.FindObservedResource(GpuResourceKind.Query, id) is { } entry
            && queryTargets.TryGetValue(entry, out var state) ? state.Revision : 0;
    }

    /// <summary>Discards the deleted name's type while active intervals retain the original allocation until EndQuery.</summary>
    internal void ForgetQueryTarget(nint id)
    {
        if (RHIResourceManagerSystem.CurrentManager?.FindResource(GpuResourceKind.Query, id) is { } entry)
            queryTargets.Remove(entry);
    }
    #endregion

    #region Native argument classification
    /// <summary>Accepts baseline targets and optional statistics only from cached context capabilities.</summary>
    internal static bool IsSupportedQueryTarget(int target, bool timestamp)
    {
        if (target == (int)QueryTarget.Timestamp) return timestamp;
        if (target == (int)QueryTarget.SamplesPassed || target == (int)QueryTarget.AnySamplesPassed
            || target == (int)QueryTarget.AnySamplesPassedConservative || target == (int)QueryTarget.TimeElapsed
            || target == (int)QueryTarget.PrimitivesGenerated || target == (int)QueryTarget.TransformFeedbackPrimitivesWritten) return true;
        return GpuSupport.HasCachedCapabilitiesForCurrentContext && GpuSupport.Graphics.PipelineStatisticsQueries
            && IsStatisticsQueryTarget(target);
    }

    /// <summary>Recognizes optional target tokens without discovering whether this context supports them.</summary>
    internal static bool IsStatisticsQueryTarget(int target)
    {
        return target == (int)ArbPipelineStatisticsQuery.VerticesSubmittedArb
            || target == (int)ArbPipelineStatisticsQuery.PrimitivesSubmittedArb
            || target == (int)ArbPipelineStatisticsQuery.VertexShaderInvocationsArb
            || target == (int)ArbPipelineStatisticsQuery.TessControlShaderPatchesArb
            || target == (int)ArbPipelineStatisticsQuery.TessEvaluationShaderInvocationsArb
            || target == (int)ArbPipelineStatisticsQuery.GeometryShaderInvocations
            || target == (int)ArbPipelineStatisticsQuery.GeometryShaderPrimitivesEmittedArb
            || target == (int)ArbPipelineStatisticsQuery.FragmentShaderInvocationsArb
            || target == (int)ArbPipelineStatisticsQuery.ComputeShaderInvocationsArb
            || target == (int)ArbPipelineStatisticsQuery.ClippingInputPrimitivesArb
            || target == (int)ArbPipelineStatisticsQuery.ClippingOutputPrimitivesArb;
    }
    #endregion
    #endregion

    #region Private
    /// <summary>Tests original-entry activity without allocating a predicate or confusing a reused numeric name.</summary>
    private bool HasActiveQuery(GpuResourceRegistration entry)
    {
        foreach (var active in observedActiveQueries.Values)
            if (ReferenceEquals(active.Entry, entry)) return true;
        return false;
    }
    /// <summary>Occlusion targets share one activity slot; boolean occlusion objects allow both boolean modes.</summary>
    private static int NormalizeQueryTarget(int target) => target == (int)QueryTarget.AnySamplesPassedConservative
        ? (int)QueryTarget.AnySamplesPassed : target;

    /// <summary>All occlusion modes compete for the same native active-query slot.</summary>
    private static int QueryActivityTarget(int target) => target == (int)QueryTarget.AnySamplesPassed
        || target == (int)QueryTarget.AnySamplesPassedConservative ? (int)QueryTarget.SamplesPassed : target;

    /// <summary>Validates indexed primitive streams using admission-time limits only.</summary>
    private static bool IsQueryIndexValid(int target, uint index)
    {
        if (target != (int)QueryTarget.PrimitivesGenerated && target != (int)QueryTarget.TransformFeedbackPrimitivesWritten)
            return index == 0;
        return index == 0 || GpuSupport.HasCachedCapabilitiesForCurrentContext && index < GpuSupport.Graphics.MaxVertexStreams;
    }

    /// <summary>Retains query execution type and issued-result revision without owning a registration lifetime.</summary>
    private sealed class QueryExecutionKnowledge(int target)
    {
        internal readonly int Target = target;
        internal ulong Revision;
    }
    #endregion
}
