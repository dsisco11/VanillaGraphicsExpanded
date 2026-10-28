using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using OpenTK.Graphics.OpenGL4;

namespace VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;

/// <summary>Owns query execution state and native results without expanding canonical metadata.</summary>
internal sealed partial class OpenGLBackend
{
    private readonly ConditionalWeakTable<GpuQuery, QueryExecution> queryExecutions = new();
    private readonly Dictionary<QueryKind, GpuQuery> activeQueries = new();
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
            if (owner.QueryKind == QueryKind.Timestamp || activeQueries.ContainsKey(owner.QueryKind))
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
            if (owner.QueryKind != QueryKind.Timestamp) throw new ArgumentException("A timestamp query is required.");
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
    public GpuQueryMetadata? InitializeQuery(QueryKind kind, out nint id)
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
    public bool TryReadQuery(GpuQuery owner, long revision, bool wait, out ulong result)
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
                if (!IsCurrentContext)
                { if (cache.OwnsKnowledgeFor(NativeOrigin)) cache.ForgetBufferBinding(target); }
                else
                {
                    try { OpenGLBufferBindings.BindBuffer(cache, target, previous); }
                    catch { if (cache.OwnsKnowledgeFor(NativeOrigin)) cache.ForgetBufferBinding(target); if (failure is null) throw; }
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
    private void RequireQueryKind(QueryKind kind)
    {
        _ = QueryTargetFor(kind);
        if (kind >= QueryKind.VerticesSubmitted && !Capabilities.PipelineStatistics)
            throw new NotSupportedException("Pipeline-statistics queries are unavailable.");
    }
    /// <summary>Translates project query categories explicitly.</summary>
    private static QueryTarget QueryTargetFor(QueryKind kind) => kind switch
    {
        QueryKind.TimeElapsed => QueryTarget.TimeElapsed,
        QueryKind.Timestamp => QueryTarget.Timestamp,
        QueryKind.SamplesPassed => QueryTarget.SamplesPassed,
        QueryKind.AnySamplesPassed => QueryTarget.AnySamplesPassed,
        QueryKind.AnySamplesPassedConservative => QueryTarget.AnySamplesPassedConservative,
        QueryKind.PrimitivesGenerated => QueryTarget.PrimitivesGenerated,
        QueryKind.TransformFeedbackPrimitivesWritten => QueryTarget.TransformFeedbackPrimitivesWritten,
        QueryKind.VerticesSubmitted => QueryTarget.VerticesSubmitted,
        QueryKind.PrimitivesSubmitted => QueryTarget.PrimitivesSubmitted,
        QueryKind.VertexShaderInvocations => QueryTarget.VertexShaderInvocations,
        QueryKind.TessellationControlPatches => (QueryTarget)ArbPipelineStatisticsQuery.TessControlShaderPatchesArb,
        QueryKind.TessellationEvaluationInvocations => (QueryTarget)ArbPipelineStatisticsQuery.TessEvaluationShaderInvocationsArb,
        QueryKind.GeometryShaderInvocations => (QueryTarget)ArbPipelineStatisticsQuery.GeometryShaderInvocations,
        QueryKind.GeometryPrimitivesEmitted => (QueryTarget)ArbPipelineStatisticsQuery.GeometryShaderPrimitivesEmittedArb,
        QueryKind.FragmentShaderInvocations => (QueryTarget)ArbPipelineStatisticsQuery.FragmentShaderInvocationsArb,
        QueryKind.ComputeShaderInvocations => (QueryTarget)ArbPipelineStatisticsQuery.ComputeShaderInvocationsArb,
        QueryKind.ClippingInputPrimitives => (QueryTarget)ArbPipelineStatisticsQuery.ClippingInputPrimitivesArb,
        QueryKind.ClippingOutputPrimitives => (QueryTarget)ArbPipelineStatisticsQuery.ClippingOutputPrimitivesArb,
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
