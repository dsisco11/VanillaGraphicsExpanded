using System;

using OpenTK.Graphics.OpenGL;

namespace VanillaGraphicsExpanded.Rendering;

/// <summary>
/// RAII wrapper for an elapsed-time query (glBeginQuery/glEndQuery with GL_TIME_ELAPSED).
/// </summary>
internal sealed class GpuTimerQuery : IDisposable
{
    private readonly GpuQuery query;
    private bool active;

    private GpuTimerQuery(GpuQuery query)
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
    /// Returns <c>true</c> when a time-elapsed query is currently active (between Begin/End).
    /// </summary>
    public bool IsActive => active;

    /// <summary>
    /// Creates a new elapsed-time query.
    /// </summary>
    public static GpuTimerQuery Create(string? debugName = null)
    {
        return new GpuTimerQuery(GpuQuery.Create(debugName));
    }

    /// <summary>
    /// Begins an elapsed-time query with <c>glBeginQuery(GL_TIME_ELAPSED)</c>.
    /// </summary>
    public void Begin()
    {
        if (!IsValid || active)
        {
            return;
        }

        GL.BeginQuery(QueryTarget.TimeElapsed, QueryId);
        active = true;
    }

    /// <summary>
    /// Ends an elapsed-time query with <c>glEndQuery(GL_TIME_ELAPSED)</c>.
    /// </summary>
    public void End()
    {
        if (!IsValid || !active)
        {
            return;
        }

        GL.EndQuery(QueryTarget.TimeElapsed);
        active = false;
    }

    /// <summary>
    /// Begins an elapsed-time query and returns an RAII scope that ends it on dispose.
    /// </summary>
    public Scope BeginScope()
    {
        Begin();
        return new Scope(this);
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
    /// Gets the elapsed time result (in nanoseconds) and blocks until the result is available.
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
    /// Attempts to get the elapsed time result (in nanoseconds) without blocking.
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
    /// Ends an active query (if any) and disposes the underlying query object.
    /// </summary>
    public void Dispose()
    {
        if (active)
        {
            try
            {
                GL.EndQuery(QueryTarget.TimeElapsed);
            }
            catch
            {
            }

            active = false;
        }

        query.Dispose();
    }

    /// <summary>
    /// RAII scope that ends the elapsed-time query on dispose.
    /// </summary>
    public readonly struct Scope : IDisposable
    {
        private readonly GpuTimerQuery owner;

        internal Scope(GpuTimerQuery owner)
        {
            this.owner = owner;
        }

        public void Dispose()
        {
            owner.End();
        }
    }
}

