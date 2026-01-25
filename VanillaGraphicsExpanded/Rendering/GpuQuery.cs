using System;

using OpenTK.Graphics.OpenGL;

namespace VanillaGraphicsExpanded.Rendering;

/// <summary>
/// RAII wrapper around an OpenGL query object created by <c>glGenQueries</c>.
/// Deletion is deferred to <see cref="GpuResourceManager"/> when available.
/// </summary>
internal sealed class GpuQuery : GpuResource, IDisposable
{
    private int queryId;

    protected override nint ResourceId
    {
        get => queryId;
        set => queryId = (int)value;
    }

    protected override GpuResourceKind ResourceKind => GpuResourceKind.Query;

    /// <summary>
    /// Gets the underlying OpenGL query object id.
    /// </summary>
    public int QueryId => queryId;

    /// <summary>
    /// Returns <c>true</c> when the query has a non-zero id and has not been disposed.
    /// </summary>
    public new bool IsValid => queryId != 0 && !IsDisposed;

    public override void SetDebugName(string? debugName)
    {
#if DEBUG
        if (queryId != 0)
        {
            GlDebug.TryLabel(ObjectLabelIdentifier.Query, queryId, debugName);
        }
#endif
    }

    private GpuQuery(int queryId)
    {
        this.queryId = queryId;
    }

    /// <summary>
    /// Creates a new OpenGL query object.
    /// </summary>
    public static GpuQuery Create(string? debugName = null)
    {
        int id = GL.GenQuery();
        if (id == 0)
        {
            throw new InvalidOperationException("glGenQuery returned 0.");
        }

        var query = new GpuQuery(id);
        query.SetDebugName(debugName);
        return query;
    }
}
