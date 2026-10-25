using System;

using OpenTK.Graphics.OpenGL;

namespace VanillaGraphicsExpanded.Rendering;

/// <summary>
/// RAII wrapper around an OpenGL query object created by <c>glGenQueries</c>.
/// Deletion is deferred to <see cref="GpuResourceManager"/> when available.
/// </summary>
internal sealed class GpuQuery : GpuResource<GpuQueryMetadata>, IDisposable
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

    public override void SetDebugName(string? debugName)
    {
#if DEBUG
        if (queryId != 0)
        {
            GlDebug.TryLabel(ObjectLabelIdentifier.Query, queryId, debugName);
        }
#endif
    }

    /// <summary>Declares an unpublished wrapper for manager-owned creation.</summary>
    private GpuQuery() { }

    /// <summary>
    /// Creates a new OpenGL query object.
    /// </summary>
    public static GpuQuery Create(string? debugName = null)
    {
        var value = new GpuQuery();
        try
        {
            if (!GpuResourceManager.CreateQuery(new GpuQueryInitialization(), value, out _).HasValue)
                throw new InvalidOperationException("Query creation failed.");
            value.SetDebugName(debugName);
            return value;
        }
        catch
        {
            // The manager retires provisional allocation; dispose a completed handoff too.
            value.Dispose();
            throw;
        }
    }
}
