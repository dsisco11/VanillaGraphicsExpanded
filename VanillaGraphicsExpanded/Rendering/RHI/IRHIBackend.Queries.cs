namespace VanillaGraphicsExpanded.Rendering.RHI;

/// <summary>Defines typed query execution independently of native target enums.</summary>
internal partial interface IRHIBackend
{
    /// <summary>Creates a realized query owner through manager handoff.</summary>
    RHICreationResult<GpuQuery> CreateQuery(in RHIQueryDescription description);
    /// <summary>Begins accumulation for the predeclared category.</summary>
    void BeginQuery(GpuQueryRef query);
    /// <summary>Ends accumulation for the matching active query.</summary>
    void EndQuery(GpuQueryRef query);
    /// <summary>Writes an ordered timestamp.</summary>
    void WriteTimestamp(GpuQueryRef query);
    /// <summary>Captures the issued query generation for result observation.</summary>
    RHIQueryTicket RequestQueryResult(GpuQueryRef query);
    /// <summary>Begins native conditional rendering using an issued occlusion query.</summary>
    void BeginConditionalRender(GpuQueryRef query, RHIConditionalRenderMode mode);
    /// <summary>Ends native conditional rendering without reading its result back.</summary>
    void EndConditionalRender();
}
