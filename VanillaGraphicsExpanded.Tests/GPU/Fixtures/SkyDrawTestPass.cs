using VanillaGraphicsExpanded.PBR.Atmosphere;
using VanillaGraphicsExpanded.Rendering.Pipeline;
using VanillaGraphicsExpanded.Rendering;

namespace VanillaGraphicsExpanded.Tests.GPU.Fixtures;

/// <summary>Models an independently prepared optional pass without engine registrations or ambient dispatch state.</summary>
internal sealed class SkyDrawTestPass : IDisposable
{
    private readonly string name;
    private readonly GraphicsPipeline[] pipelines;
    private readonly Action<GraphicsCommandContext> draw;
    private readonly List<string> events;
    internal bool Skipped, Disabled, Disposed;
    internal readonly List<Exception> Failures = [];

    #region Public API
    /// <summary>Retains a complete prepared footprint and its local recovery policy.</summary>
    internal SkyDrawTestPass(int index, GraphicsPipeline[] pipelines, Action<GraphicsCommandContext> draw, List<string> events)
    {
        name = "sky" + index;
        this.pipelines = pipelines;
        this.draw = draw;
        this.events = events;
    }

    /// <summary>Produces no executable work for a skipped, failed or disposed source.</summary>
    internal SkyDraw? PrepareDraw()
    {
        events.Add("prepare:" + name);
        if (Skipped || Disabled || Disposed) return null;
        try { return new(name, pipelines, draw, SubmissionFailed); }
        catch (Exception error) when (!EngineBoundaryRestoreException.IsRestorationFailure(error))
        {
            SubmissionFailed(error);
            return null;
        }
    }

    /// <summary>Withdraws only this source after its own validation or drawing failure.</summary>
    internal void SubmissionFailed(Exception error)
    {
        Failures.Add(error);
        Disabled = true;
    }

    /// <summary>Re-enables the source through an explicit recovery operation.</summary>
    internal void Recover() => Disabled = false;

    /// <summary>Prevents later preparation after the source's lifetime ends.</summary>
    public void Dispose() => Disposed = true;
    #endregion
}
