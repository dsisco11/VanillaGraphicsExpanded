namespace VanillaGraphicsExpanded.Rendering.RHI;

/// <summary>Exposes query creation, ordered execution and explicit result requests.</summary>
public sealed partial class RHICommandContext
{
    #region Public API
    /// <summary>Creates a query for one immutable category.</summary>
    public RHICreationResult<GpuQuery> CreateQuery(in RHIQueryDescription description) => backend.CreateQuery(description);
    /// <summary>Begins a non-timestamp query.</summary>
    public void BeginQuery(GpuQueryRef query) => backend.BeginQuery(query);
    /// <summary>Ends the matching active query.</summary>
    public void EndQuery(GpuQueryRef query) => backend.EndQuery(query);
    /// <summary>Writes a timestamp without waiting for its result.</summary>
    public void WriteTimestamp(GpuQueryRef query) => backend.WriteTimestamp(query);
    /// <summary>Creates a result request that never silently follows query reissue.</summary>
    public RHIQueryTicket RequestQueryResult(GpuQueryRef query) => backend.RequestQueryResult(query);
    /// <summary>Begins native conditional rendering without a CPU query-result read.</summary>
    public void BeginConditionalRender(GpuQueryRef query, ConditionalRenderPolicy mode) => backend.BeginConditionalRender(query, mode);
    /// <summary>Ends the explicitly active native condition.</summary>
    public void EndConditionalRender() => backend.EndConditionalRender();
    #endregion
}
