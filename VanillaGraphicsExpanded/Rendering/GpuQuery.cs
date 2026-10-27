using VanillaGraphicsExpanded.Rendering.RHI;
using System;


namespace VanillaGraphicsExpanded.Rendering;

/// <summary>
/// RAII wrapper around an OpenGL query object created by <c>glGenQueries</c>.
/// Deletion is deferred to <see cref="GpuResourceManager"/> when available.
/// </summary>
public sealed class GpuQuery : GpuResource<GpuQueryMetadata>, IDisposable
{
    private int queryId;

    /// <inheritdoc />
    public override nint ResourceId
    {
        get => queryId;
        protected set => queryId = (int)value;
    }

    protected override GpuResourceKind ResourceKind => GpuResourceKind.Query;

    /// <summary>
    /// Gets the underlying OpenGL query object id.
    /// </summary>
    public int QueryId => queryId;

    /// <summary>
    /// Returns <c>true</c> when the query has a non-zero id and has not been disposed.
    /// </summary>
    public new bool IsValid => base.IsValid;

    /// <inheritdoc />
    public override void SetDebugName(string? debugName)
    {
        RHICommandContext.TryLabelResource(this, debugName);
    }

    /// <summary>Declares an unpublished wrapper for manager-owned creation.</summary>
    internal GpuQuery(QueryKind kind) { QueryKind = kind; }

    /// <summary>Gets immutable creation intent, without native discovery.</summary>
    internal QueryKind QueryKind { get; }

    /// <summary>Creates a consumer-only reference without transferring ownership.</summary>
    public static implicit operator GpuQueryRef(GpuQuery? resource) => new(resource);

}
