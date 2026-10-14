using System;
using System.Threading.Tasks;
using System.Threading;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Rendering.Shaders;
using Vintagestory.API.Client;

namespace VanillaGraphicsExpanded.PBR.Atmosphere.Celestial;

/// <summary>Schedules load-time source builds and atomically retains the last complete resource set on replacement failures.</summary>
internal sealed class CelestialSourceLifetime : IDisposable
{
    private readonly ICoreClientAPI api;
    private Task<CelestialSource>? preparation;
    private CelestialGalaxyGeneration? candidate;
    private int requested = -1;
    private bool loading, disposed;
    private CancellationTokenSource? cancellation;
    private (nint Handle, long Generation) context;
    /// <summary>Completed candidate retained until matching atmospheric illumination is uploaded successfully.</summary>
    internal CelestialResourceSet? Ready { get; private set; }
    /// <summary>Returns borrowed complete resources; consumers must not dispose or pair these with another source identity.</summary>
    internal CelestialResourceSet? Completed { get; private set; }

    #region Public API
    #region Publication
    /// <summary>Retains only the API needed for current-context shader loading and failure reporting.</summary>
    internal CelestialSourceLifetime(ICoreClientAPI api) { this.api = api; }
    /// <summary>Commits the matching source only after atmospheric upload succeeds, then retires the prior display set.</summary>
    internal void Commit(CelestialSource source)
    {
        if (ReferenceEquals(Completed?.Source, source)) return;
        if (!ReferenceEquals(Ready?.Source, source)) throw new InvalidOperationException("Unmatched celestial publication.");
        var old = Completed; Completed = Ready; Ready = null; old?.Dispose();
    }
    #endregion
    #region World loading
    /// <summary>Starts source preparation during world loading without running GPU operations on a worker thread.</summary>
    internal void StartLoading(int quality)
    {
        if (loading && requested == quality) return;
        loading = true; Request(quality);
    }
    /// <summary>Submits the initial GPU work before the world loading callback returns, with no GPU completion wait.</summary>
    internal void FinishLoading(int quality)
    {
        if (!loading) StartLoading(quality);
        if (requested != quality) Request(quality);
        try
        {
            // Waiting for bounded CPU input preparation here keeps initial raster submission inside world loading.
            preparation?.GetAwaiter().GetResult();
            Update(quality);
        }
        catch (Exception error) when (!EngineBoundaryRestoreException.IsRestorationFailure(error)) { Fail(error); }
    }
    /// <summary>Polls completion and relevant quality changes; unchanged ordinary frames allocate and regenerate nothing.</summary>
    internal void Update(int quality)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        if (!loading) return;
        var current = Rendering.Integration.RenderContextRegistry.Current();
        if (context != default && current != context)
        {
            // Dead namespaces cannot retain a display publication. Resource owners abandon
            // their old handles; a recovered context starts with zero owned contribution.
            candidate?.Dispose(); candidate = null; Completed?.Dispose(); Completed = null;
            Ready?.Dispose(); Ready = null;
            cancellation?.Cancel(); preparation = null;
            requested = -1;
        }
        context = current;
        if (current == default) return;
        // A fenced candidate remains stable while its matching atmosphere is pending.
        if (Ready is not null) return;
        if (requested != quality) Request(quality);
        try
        {
            if (candidate is not null)
            {
                var ready = candidate.TryComplete();
                if (ready is null) return;
                Ready = ready;
                candidate.Dispose(); candidate = null;
            }
            if (preparation is not { IsCompleted: true } task) return;
            var source = task.GetAwaiter().GetResult(); preparation = null;
            var shader = GpuShaderProgramLibraries.Get<CelestialGenerationProgram>(api, "celestial_generate");
            if (shader?.EnsureReady() != true) throw new InvalidOperationException("Celestial generation executable is unavailable.");
            candidate = new(shader, source); candidate.Submit();
        }
        catch (Exception error) when (!EngineBoundaryRestoreException.IsRestorationFailure(error)) { Fail(error); }
    }
    #endregion
    #region Recovery and disposal
    /// <summary>Withdraws pending work on reload, retaining the complete map until its new replacement succeeds.</summary>
    internal void Reload(int quality)
    {
        Ready?.Dispose(); Ready = null;
        if (loading) { requested = -1; Request(quality); }
    }
    /// <summary>Clears world publication and retires pending and completed GPU storage on the owning context.</summary>
    public void Dispose()
    {
        if (disposed) return;
        disposed = true; preparation = null;
        cancellation?.Cancel(); cancellation?.Dispose(); cancellation = null;
        candidate?.Dispose(); candidate = null; Completed?.Dispose(); Completed = null;
        Ready?.Dispose(); Ready = null;
    }
    #endregion
    #endregion

    #region Private
    /// <summary>Captures a new immutable quality key while discarded workers own no GL resources or publication references.</summary>
    private void Request(int quality)
    {
        var budget = CelestialQuality.FromLevel(quality);
        requested = quality; candidate?.Dispose(); candidate = null;
        cancellation?.Cancel(); cancellation?.Dispose(); cancellation = new();
        var token = cancellation.Token;
        preparation = Task.Run(() => CelestialSource.Create(budget, token), token);
    }
    /// <summary>Logs a failed candidate once, retrying only on a relevant input change or explicit reload.</summary>
    private void Fail(Exception error)
    {
        preparation = null; candidate?.Dispose(); candidate = null;
        api.Logger.Error("[VGE] Celestial source generation failed; retaining the last complete source. {0}", error);
    }
    #endregion
}
