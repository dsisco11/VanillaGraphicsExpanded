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

    private GpuQuery(int queryId)
    {
        this.queryId = queryId;
        GpuResourceManager.RegisterEmptyMetadata(this);
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

        return CompleteOwnedCreation(GpuResourceKind.Query, id, () =>
        {
            var value = new GpuQuery(id);
            value.SetDebugName(debugName);
            return value;
        });
    }
}
