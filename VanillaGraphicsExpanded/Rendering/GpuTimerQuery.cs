using VanillaGraphicsExpanded.Rendering.RHI;
using System;

using OpenTK.Graphics.OpenGL;

namespace VanillaGraphicsExpanded.Rendering;

/// <summary>
/// RAII wrapper for an elapsed-time query (glBeginQuery/glEndQuery with GL_TIME_ELAPSED).
/// </summary>
internal sealed class GpuTimerQuery : IDisposable
{
    private readonly GpuQuery query;
    private RHIQueryTicket? result;
    private bool active;
    private RHICommandContext? activeContext;

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
        return new GpuTimerQuery(RHICommandContext.Current.CreateQuery(new(QueryKind.TimeElapsed, debugName)).RequireResource());
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

        activeContext = RHICommandContext.Current;
        activeContext.BeginQuery(query);
        result = null;
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

        var context = activeContext!;
        context.EndQuery(query);
        active = false;
        activeContext = null;
        result = context.RequestQueryResult(query);
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

        return result?.Poll() == CompletionStatus.Ready;
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

        if (result is null || result.Wait() != CompletionStatus.Ready)
            throw new InvalidOperationException("Query completion failed.", result?.Error);
        return checked((long)result.Result);
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
                activeContext!.EndQuery(query);
            }
            finally
            {
                // A rejected foreign-context unwind keeps the interval retryable. A completed or
                // retired-origin unwind releases its guard even when result reconciliation fails.
                if (query.UsageReferences.Pass == 0) { active = false; activeContext = null; }
            }
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

