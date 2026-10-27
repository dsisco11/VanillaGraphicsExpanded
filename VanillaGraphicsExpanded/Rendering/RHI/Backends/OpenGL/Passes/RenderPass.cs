using System.Linq;
using VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;
using VanillaGraphicsExpanded.Rendering.RHI;
using System;
using System.Collections.Generic;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.Rendering.Pipeline.Descriptions;
using VanillaGraphicsExpanded.Rendering.Pipeline.State;

namespace VanillaGraphicsExpanded.Rendering.Pipeline.Passes;

/// <summary>Owns immediate target routing and borrowed lifetime inside an existing engine restoration boundary.</summary>
internal sealed partial class RenderPass : IDisposable
{
    [ThreadStatic] private static RenderPass? active;
    private readonly RenderPassDesc description;
    private readonly EngineBoundaryScope boundary;
    private readonly RenderPassTargets targets;
    private bool disposed;
    internal RenderTargetSignature Signature => targets.Signature;
    internal RenderArea Area => targets.Area;
    internal RHIViewport Viewport => new(Area.X, Area.Y, Area.Width, Area.Height);

    #region Public API
    /// <summary>Validates metadata and intentions before setup; optional sequence ownership defers routing restoration.</summary>
    internal static RenderPass Begin(RenderPassDesc description, EngineBoundaryScope boundary, RenderPassSequence sequence)
    {
        ArgumentNullException.ThrowIfNull(description);
        var cache = StateCache.Current;
        cache.RequireEngineBoundary(boundary);
        var required = OpenGLRenderPassOperations.ClearCoverage.Union(new(dynamic: DynamicDrawStateKnowledge.Viewport));
        if (!boundary.Snapshot.Coverage.Contains(required, GpuSupport.Graphics.MaxDrawBuffers))
            throw new InvalidOperationException("Render passes require complete graphics restoration coverage.");
        if (active is not null) throw new InvalidOperationException("Render passes cannot nest.");
        var pass = new RenderPass(description, boundary, sequence);
        boundary.AddCleanup(EngineBoundaryCleanup.Framebuffers, pass, pass.Abandon);
        return pass;
    }

    /// <summary>Releases CPU target borrows after context loss without restoring framebuffer routing or bindings.</summary>
    internal void Abandon()
    {
        if (disposed) return;
        disposed = true;
        targets.Dispose();
        if (ReferenceEquals(active, this)) active = null;
    }

    /// <summary>Checks active authority and image lifetime without querying native state.</summary>
    internal void Validate()
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        if (!ReferenceEquals(active, this)) throw new InvalidOperationException("Render pass is not active.");
        StateCache.Current.RequireEngineBoundary(boundary);
        targets.Validate();
    }

    /// <summary>Allows pipeline reuse across target identities and sizes while requiring exact format/sample compatibility.</summary>
    internal void ValidatePipeline(RHIGraphicsPipeline pipeline)
    {
        Validate();
        pipeline.ValidateTargets(Signature);
    }

    /// <summary>Ends image authority while the admitted sequence retains external routing restoration.</summary>
    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        // Output-store discard remains a permission; retaining contents preserves packed aspects and partial areas.
        try { targets.Dispose(); }
        finally { active = null; }
    }
    #endregion

    #region Private
    /// <summary>Acquires resources first, then changes routing and applies validated load operations.</summary>
    private RenderPass(RenderPassDesc description, EngineBoundaryScope boundary, RenderPassSequence sequence)
    {
        this.description = description; this.boundary = boundary;
        targets = new(description);
        try
        {
            using var errors = new GlDebug.ErrorScope("Render pass setup");
            sequence.Bind(description.Target,description.Routing.ToArray());
            StateCache.Current.ApplyDynamic(new DynamicDrawState { X = Area.X, Y = Area.Y, Width = Area.Width, Height = Area.Height });
            OpenGLRenderPassOperations.Load(description, Area);
            active = this;
        }
        catch (Exception operationFailure)
        {
            try {targets.Dispose();}
            catch(Exception cleanupFailure){throw new AggregateException(operationFailure,cleanupFailure);}
            finally {active=null;}
            throw;
        }
    }

    #endregion
}
