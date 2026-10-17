using System;
using System.Reactive.Linq;
using VanillaGraphicsExpanded.Rendering;
using Vintagestory.API.Client;

namespace VanillaGraphicsExpanded.PBR;

/// <summary>Binds shared observable consumer demand to the hierarchy renderer's resource transitions.</summary>
internal sealed class DepthHierarchyEnablement : IDisposable
{
    private readonly PipelineEnablement sources;
    private readonly bool ownsSources;
    private readonly DepthHierarchyRenderer renderer;
    private readonly IDisposable subscription;
    private bool disposed;

    #region Public API
    /// <summary>Creates a standalone source lifetime when no shared pipeline composition is available.</summary>
    internal DepthHierarchyEnablement(ICoreClientAPI api, DepthHierarchyRenderer renderer)
        : this(new PipelineEnablement(api), renderer, true) { }

    /// <summary>Binds to the conditions already shared by ambient occlusion and other pipeline consumers.</summary>
    internal DepthHierarchyEnablement(PipelineEnablement sources, DepthHierarchyRenderer renderer)
        : this(sources, renderer, false) { }

    /// <summary>Detaches demand before retiring the renderer and any privately owned sources.</summary>
    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        subscription.Dispose();
        try { renderer.SetEnabled(false); }
        finally { if (ownsSources) sources.Dispose(); }
    }
    #endregion

    #region Private
    /// <summary>Applies the replayed initial state synchronously and owns only this renderer's subscription.</summary>
    private DepthHierarchyEnablement(PipelineEnablement sources, DepthHierarchyRenderer renderer, bool ownsSources)
    {
        this.sources = sources; this.renderer = renderer; this.ownsSources = ownsSources;
        subscription = sources.DepthHierarchy.Subscribe(renderer.SetEnabled);
    }
    #endregion
}
