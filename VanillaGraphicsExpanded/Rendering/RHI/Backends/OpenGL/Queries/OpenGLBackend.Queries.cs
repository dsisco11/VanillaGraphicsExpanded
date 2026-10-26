using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using OpenTK.Graphics.OpenGL4;

namespace VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;

/// <summary>Owns query execution state and native results without expanding canonical metadata.</summary>
internal sealed partial class OpenGLBackend
{
    private readonly ConditionalWeakTable<GpuQuery, QueryExecution> queryExecutions = new();
    private readonly Dictionary<RHIQueryKind, GpuQuery> activeQueries = new();
    private bool queryTransition;
    #region Public API
    /// <inheritdoc />
    public RHICreationResult<GpuQuery> CreateQuery(in RHIQueryDescription description)
    {
        RequireContext();
        RequireQueryKind(description.Kind);
        var owner = new GpuQuery(description.Kind);
        try
        {
            if (!GpuResourceManager.CreateQuery(new(description.Kind), owner, out _).HasValue)
            { owner.Dispose(); return new(null, "Query initialization failed."); }
            owner.SetDebugName(description.Label);
            return new(owner, null);
        }
        catch { try { owner.Dispose(); } catch { } throw; }
    }
    /// <inheritdoc />
    public void BeginQuery(GpuQueryRef query)
    {
        // Native callbacks must not reenter an unfinished execution-state transition.
        if (queryTransition) throw new InvalidOperationException("An auxiliary operation is already in progress.");
        queryTransition = true;
        try
        {
            var owner = RequireQuery(query);
            if (owner.QueryKind == RHIQueryKind.Timestamp || activeQueries.ContainsKey(owner.QueryKind))
                throw new InvalidOperationException("The query category is already active or is timestamp-only.");
            var state = queryExecutions.GetOrCreateValue(owner);
            state.Issued = false;
            state.Revision++;
            owner.RetainPassReference();
            try
            {
                GL.BeginQuery(QueryTargetFor(owner.QueryKind), owner.QueryId);
                RequireContext();
                activeQueries.Add(owner.QueryKind, owner);
            }
            catch { owner.ReleasePassReference(); throw; }
        }
        finally { queryTransition = false; }
    }
    /// <inheritdoc />
    public void EndQuery(GpuQueryRef query)
    {
        // Native callbacks must not reenter an unfinished execution-state transition.
        if (queryTransition) throw new InvalidOperationException("An auxiliary operation is already in progress.");
        queryTransition = true;
        try
        {
            if (HasRetiredOrigin)
            {
                var retired = query.RequireResource();
                if (!activeQueries.TryGetValue(retired.QueryKind, out var captured) || !ReferenceEquals(retired, captured))
                    throw new InvalidOperationException("The matching query is not active.");
                activeQueries.Remove(retired.QueryKind);
                queryExecutions.GetOrCreateValue(retired).Issued = false;
                retired.ReleasePassReference();
                return;
            }
            var owner = RequireQuery(query);
            if (!activeQueries.TryGetValue(owner.QueryKind, out var active) || !ReferenceEquals(owner, active))
                throw new InvalidOperationException("The matching query is not active.");
            try
            {
                GL.EndQuery(QueryTargetFor(owner.QueryKind));
                RequireContext();
                queryExecutions.GetOrCreateValue(owner).Issued = true;
            }
            finally { activeQueries.Remove(owner.QueryKind); owner.ReleasePassReference(); }
        }
        finally { queryTransition = false; }
    }
    /// <inheritdoc />
    public void WriteTimestamp(GpuQueryRef query)
    {
        // Native callbacks must not reenter an unfinished execution-state transition.
        if (queryTransition) throw new InvalidOperationException("An auxiliary operation is already in progress.");
        queryTransition = true;
        try
        {
            var owner = RequireQuery(query);
            if (owner.QueryKind != RHIQueryKind.Timestamp) throw new ArgumentException("A timestamp query is required.");
            var state = queryExecutions.GetOrCreateValue(owner);
            state.Issued = false;
            state.Revision++;
            GL.QueryCounter(owner.QueryId, QueryCounterTarget.Timestamp);
            RequireContext();
            state.Issued = true;
        }
        finally { queryTransition = false; }
    }
    /// <inheritdoc />
    public RHIQueryTicket RequestQueryResult(GpuQueryRef query)
    {
        var owner = RequireQuery(query);
        var state = queryExecutions.GetOrCreateValue(owner);
        if (!state.Issued) throw new InvalidOperationException("The query has no completed submission.");
        return new(this, owner, state.Revision);
    }
    #endregion
    #region Internal API
    /// <summary>Realizes the target-specific query without a temporary begin/end or readback.</summary>
    internal GpuQueryMetadata? InitializeQuery(RHIQueryKind kind, out nint id)
    {
        RequireContext();
        RequireQueryKind(kind);
        int name = 0;
        try { GL.CreateQueries(QueryTargetFor(kind), 1, out name); }
        finally { id = name; }
        RequireContext();
        return name == 0 ? null : new GpuQueryMetadata();
    }
    /// <summary>Reads only a submitted generation; availability polling never requests a blocking result.</summary>
    internal bool TryReadQuery(GpuQuery owner, long revision, bool wait, out ulong result)
    {
        // Native callbacks must not reenter an unfinished execution-state transition.
        if (queryTransition) throw new InvalidOperationException("An auxiliary operation is already in progress.");
        queryTransition = true;
        try
        {
    RequireQuery(owner);
            var state = queryExecutions.GetOrCreateValue(owner);
            if (!state.Issued || state.Revision != revision) throw new InvalidOperationException("The query request was superseded.");
            var target = OpenTK.Graphics.OpenGL.BufferTarget.QueryBuffer;
            if (!cache.TryGetCachedBoundBuffer(target, out int previous))
            {
                previous = GL.GetInteger((GetPName)All.QueryBufferBinding);
                RequireContext();
                cache.EstablishBufferBinding(target, previous);
            }
            Exception? failure = null;
            result = 0;
            try
            {
                OpenGLBufferBindings.BindBuffer(cache, target, 0);
                if (!wait)
                {
                    GL.GetQueryObject(owner.QueryId, GetQueryObjectParam.QueryResultAvailable, out int available);
                    RequireContext();
                    if (available == 0) return false;
                    RequireQuery(owner);
                    if (state.Revision != revision) throw new InvalidOperationException("The query request was superseded during observation.");
                }
                GL.GetQueryObject(unchecked((uint)owner.QueryId), GetQueryObjectParam.QueryResult, out result);
                RequireContext();
                return true;
            }
            catch (Exception error) { failure = error; throw; }
            finally
            {
                if (!IsCurrentContext) cache.ForgetBufferBinding(target);
                else
                {
                    try { OpenGLBufferBindings.BindBuffer(cache, target, previous); }
                    catch { cache.ForgetBufferBinding(target); if (failure is null) throw; }
                }
            }
        }
        finally { queryTransition = false; }
    }
    #endregion
    #region Private
    /// <summary>Requires original identity before inspecting execution state or native results.</summary>
    private GpuQuery RequireQuery(GpuQueryRef reference)
    {
        RequireContext();
        var owner = reference.RequireResource();
        owner.RequireOriginalContext();
        if (!owner.IsValid) throw new InvalidOperationException("The original query has retired.");
        cache.RequireResourceOperationAvailable(GpuResourceKind.Query, owner.ResourceId);
        return owner;
    }
    /// <summary>Rejects optional statistics before issuing creation or beginning work.</summary>
    private void RequireQueryKind(RHIQueryKind kind)
    {
        _ = QueryTargetFor(kind);
        if (kind >= RHIQueryKind.VerticesSubmitted && !Capabilities.PipelineStatistics)
            throw new NotSupportedException("Pipeline-statistics queries are unavailable.");
    }
    /// <summary>Translates project query categories explicitly.</summary>
    private static QueryTarget QueryTargetFor(RHIQueryKind kind) => kind switch
    {
        RHIQueryKind.TimeElapsed => QueryTarget.TimeElapsed,
        RHIQueryKind.Timestamp => QueryTarget.Timestamp,
        RHIQueryKind.SamplesPassed => QueryTarget.SamplesPassed,
        RHIQueryKind.AnySamplesPassed => QueryTarget.AnySamplesPassed,
        RHIQueryKind.AnySamplesPassedConservative => QueryTarget.AnySamplesPassedConservative,
        RHIQueryKind.PrimitivesGenerated => QueryTarget.PrimitivesGenerated,
        RHIQueryKind.TransformFeedbackPrimitivesWritten => QueryTarget.TransformFeedbackPrimitivesWritten,
        RHIQueryKind.VerticesSubmitted => QueryTarget.VerticesSubmitted,
        RHIQueryKind.PrimitivesSubmitted => QueryTarget.PrimitivesSubmitted,
        RHIQueryKind.VertexShaderInvocations => QueryTarget.VertexShaderInvocations,
        RHIQueryKind.TessellationControlPatches => (QueryTarget)ArbPipelineStatisticsQuery.TessControlShaderPatchesArb,
        RHIQueryKind.TessellationEvaluationInvocations => (QueryTarget)ArbPipelineStatisticsQuery.TessEvaluationShaderInvocationsArb,
        RHIQueryKind.GeometryShaderInvocations => (QueryTarget)ArbPipelineStatisticsQuery.GeometryShaderInvocations,
        RHIQueryKind.GeometryPrimitivesEmitted => (QueryTarget)ArbPipelineStatisticsQuery.GeometryShaderPrimitivesEmittedArb,
        RHIQueryKind.FragmentShaderInvocations => (QueryTarget)ArbPipelineStatisticsQuery.FragmentShaderInvocationsArb,
        RHIQueryKind.ComputeShaderInvocations => (QueryTarget)ArbPipelineStatisticsQuery.ComputeShaderInvocationsArb,
        RHIQueryKind.ClippingInputPrimitives => (QueryTarget)ArbPipelineStatisticsQuery.ClippingInputPrimitivesArb,
        RHIQueryKind.ClippingOutputPrimitives => (QueryTarget)ArbPipelineStatisticsQuery.ClippingOutputPrimitivesArb,
        _ => throw new ArgumentOutOfRangeException(nameof(kind))
    };
    /// <summary>Tracks submitted result identity, not resource metadata or ownership.</summary>
    private sealed class QueryExecution
    {
        internal long Revision;
        internal bool Issued;
        /// <summary>Initializes an unissued query state.</summary>
        public QueryExecution() { }
    }
    #endregion
}
