using System.Linq;
using VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;
using VanillaGraphicsExpanded.Rendering.RHI;
using System;
using System.Collections.Generic;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.Rendering.Pipeline.Descriptions;
using VanillaGraphicsExpanded.Rendering.Pipeline.State;

namespace VanillaGraphicsExpanded.Rendering.Pipeline.Passes;

/// <summary>Executes immediate target effects inside an existing engine restoration boundary.</summary>
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
    internal static RenderPass Begin(RenderPassDesc description, EngineBoundaryScope boundary, RenderPassSequence sequence, RenderPassTargets prepared)
    {
        ArgumentNullException.ThrowIfNull(description);
        var cache = OpenGlStateCache.Current;
        cache.RequireEngineBoundary(boundary);
        var required = OpenGLRenderPassOperations.ClearCoverage.Union(new(dynamic: DynamicDrawStateKnowledge.Viewport));
        if (!boundary.Snapshot.Coverage.Contains(required, GpuSupport.Graphics.MaxDrawBuffers))
            throw new InvalidOperationException("Render passes require complete graphics restoration coverage.");
        if (active is not null) throw new InvalidOperationException("Render passes cannot nest.");
        var pass = new RenderPass(description, boundary, sequence, prepared);
        boundary.AddCleanup(EngineBoundaryCleanup.Framebuffers, pass, pass.Abandon);
        return pass;
    }

    /// <summary>Abandons the pass after context loss without issuing native restoration.</summary>
    internal void Abandon()
    {
        if (disposed) return;
        disposed = true;

        if (ReferenceEquals(active, this)) active = null;
    }

    /// <summary>Checks active pass sequencing and optional owner diagnostics without querying native state.</summary>
    internal void Validate()
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        if (!ReferenceEquals(active, this)) throw new InvalidOperationException("Render pass is not active.");
        OpenGlStateCache.Current.RequireEngineBoundary(boundary);
        targets.Validate();
    }

    /// <summary>Allows pipeline reuse across target identities and sizes while requiring exact format/sample compatibility.</summary>
    internal void ValidatePipeline(RHIGraphicsPipeline pipeline)
    {
        Validate();
        targets.PreparePipeline(pipeline);
    }

    /// <summary>Ends image authority while the admitted sequence retains external routing restoration.</summary>
    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        // Output-store discard remains a permission; retaining contents preserves packed aspects and partial areas.
        active = null;
    }
    #endregion

    #region Private
    /// <summary>Consumes prepared target facts, then executes routing and every requested load operation.</summary>
    private RenderPass(RenderPassDesc description, EngineBoundaryScope boundary, RenderPassSequence sequence, RenderPassTargets prepared)
    {
        this.description = description; this.boundary = boundary;
        targets = prepared;
        try
        {
            using var errors = new GlDebug.ErrorScope("Render pass setup");
            sequence.Bind(description.Target,description.Routing.ToArray());
            OpenGlStateCache.Current.ApplyDynamic(new DynamicDrawState { X = Area.X, Y = Area.Y, Width = Area.Width, Height = Area.Height });
            OpenGLRenderPassOperations.Load(description, Area);
            active = this;
        }
        catch
        {
            active = null;
            throw;
        }
    }

    #endregion
}
