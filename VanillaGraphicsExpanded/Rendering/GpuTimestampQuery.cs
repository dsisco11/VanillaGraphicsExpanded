using System;

using OpenTK.Graphics.OpenGL;

namespace VanillaGraphicsExpanded.Rendering;

/// <summary>
/// RAII wrapper for a timestamp query (glQueryCounter with GL_TIMESTAMP).
/// </summary>
internal sealed class GpuTimestampQuery : IDisposable
{
    private readonly GpuQuery query;

    private GpuTimestampQuery(GpuQuery query)
    {
        this.query = query;
    }

    /// <summary>
    /// Gets the underlying OpenGL query object id.
    /// </summary>
    public int QueryId => query.QueryId;

    /// <summary>
    /// Returns <c>true</c> when the query has a non-zero id and has not been disposed.
    /// </summary>
    public bool IsValid => query.IsValid;

    /// <summary>
    /// Creates a new timestamp query.
    /// </summary>
    public static GpuTimestampQuery Create(string? debugName = null)
    {
        return new GpuTimestampQuery(GpuQuery.Create(debugName));
    }

    /// <summary>
    /// Issues a timestamp query with <c>glQueryCounter(GL_TIMESTAMP)</c>.
    /// </summary>
    public void Issue()
    {
        if (!IsValid)
        {
            return;
        }

        GL.QueryCounter(QueryId, QueryCounterTarget.Timestamp);
    }

    /// <summary>
    /// Returns <c>true</c> if the query result is available.
    /// </summary>
    public bool IsResultAvailable()
    {
        if (!IsValid)
        {
            return false;
        }

        GL.GetQueryObject(QueryId, GetQueryObjectParam.QueryResultAvailable, out int available);
        return available != 0;
    }

    /// <summary>
    /// Gets the timestamp result (in nanoseconds) and blocks until the result is available.
    /// </summary>
    public long GetResultNanoseconds()
    {
        if (!IsValid)
        {
            return 0;
        }

        GL.GetQueryObject(QueryId, GetQueryObjectParam.QueryResult, out long result);
        return result;
    }

    /// <summary>
    /// Attempts to get the timestamp result (in nanoseconds) without blocking.
    /// </summary>
    public bool TryGetResultNanoseconds(out long result)
    {
        if (!IsResultAvailable())
        {
            result = 0;
            return false;
        }

        result = GetResultNanoseconds();
        return true;
    }

    /// <summary>
    /// Sets a debug label for the underlying query object (best-effort; typically only active in debug builds).
    /// </summary>
    public void SetDebugName(string? debugName)
    {
        query.SetDebugName(debugName);
    }

    /// <summary>
    /// Disposes the underlying query object (deferred deletion when the GPU resource manager is active).
    /// </summary>
    public void Dispose()
    {
        query.Dispose();
    }
}

