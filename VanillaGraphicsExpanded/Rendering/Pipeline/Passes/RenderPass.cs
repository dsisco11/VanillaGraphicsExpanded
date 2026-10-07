using System;
using System.Collections.Generic;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.Rendering.Pipeline.Descriptions;
using VanillaGraphicsExpanded.Rendering.Pipeline.State;

namespace VanillaGraphicsExpanded.Rendering.Pipeline.Passes;

/// <summary>Owns immediate target routing and borrowed lifetime inside an existing engine restoration boundary.</summary>
internal sealed class RenderPass : IDisposable
{
    [ThreadStatic] private static RenderPass? active;
    private readonly RenderPassDesc description;
    private readonly EngineBoundaryScope boundary;
    private readonly RenderPassTargets targets;
    private readonly StateCache.FramebufferScope bindings;
    private readonly DrawBuffersEnum[] previousRouting;
    private bool disposed;
    internal RenderTargetSignature Signature => targets.Signature;
    internal RenderArea Area => targets.Area;
    internal DynamicDrawState Viewport => new() { X = Area.X, Y = Area.Y, Width = Area.Width, Height = Area.Height };

    #region Public API
    /// <summary>Validates metadata and intentions before setup; failed setup restores routing and borrowed bindings.</summary>
    internal static RenderPass Begin(RenderPassDesc description, EngineBoundaryScope boundary)
    {
        ArgumentNullException.ThrowIfNull(description);
        var cache = StateCache.Current;
        cache.RequireEngineBoundary(boundary);
        var required = new PipelineStateCoverage(depth: DepthStateKnowledge.WriteEnabled,
            rasterizer: RasterizerStateKnowledge.ScissorEnabled, dynamic: DynamicDrawStateKnowledge.Viewport,
            globalBlend: BlendStateKnowledge.WriteMask, completeGraphics: true);
        if (!boundary.Snapshot.Coverage.Contains(required, GpuSupport.Graphics.MaxDrawBuffers))
            throw new InvalidOperationException("Render passes require complete graphics restoration coverage.");
        if (active is not null) throw new InvalidOperationException("Render passes cannot nest.");
        var pass = new RenderPass(description, boundary);
        boundary.AddCleanup(EngineBoundaryCleanup.Framebuffers, pass);
        return pass;
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
    internal void ValidatePipeline(GraphicsPipeline pipeline)
    {
        Validate();
        pipeline.ValidateTargets(Signature);
    }

    /// <summary>Ends target authority and restores routing/bindings without disposing borrowed storage.</summary>
    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        var failures = new List<Exception>();
        try
        {
            // Store discard permits undefined contents. Retaining them is a legal conservative
            // implementation, including partial areas and independently preserved packed aspects.
            try { RestoreRouting(); }
            catch (Exception error) { failures.Add(error); }
            try { bindings.Dispose(); }
            catch (Exception error) { failures.Add(error); }
        }
        finally { targets.Dispose(); active = null; }
        if (failures.Count != 0) throw new EngineBoundaryRestoreException("Render pass cleanup failed.", new AggregateException(failures));
    }
    #endregion

    #region Private
    /// <summary>Acquires resources first, then changes routing and applies validated load operations.</summary>
    private RenderPass(RenderPassDesc description, EngineBoundaryScope boundary)
    {
        this.description = description; this.boundary = boundary;
        targets = new(description);
        try { bindings = StateCache.Current.BindFramebufferScope(); }
        catch { targets.Dispose(); throw; }
        previousRouting = new DrawBuffersEnum[GpuSupport.Graphics.MaxDrawBuffers];
        bool routingCaptured = false;
        try
        {
            using var errors = new GlDebug.ErrorScope("Render pass setup");
            description.Target.Bind();
            for (int i = 0; i < previousRouting.Length; i++)
                previousRouting[i] = (DrawBuffersEnum)GL.GetInteger(GetPName.DrawBuffer0 + i);
            GlDebug.ThrowIfErrors("Render pass routing capture");
            routingCaptured = true;
            var routing = new DrawBuffersEnum[Math.Max(1, description.Colors.Count)];
            for (int i = 0; i < description.Colors.Count; i++)
                routing[i] = description.Colors[i].Attachment < 0 ? DrawBuffersEnum.None
                    : DrawBuffersEnum.ColorAttachment0 + description.Colors[i].Attachment;
            GL.DrawBuffers(routing.Length, routing);
            description.Target.ValidatePassCompleteness(routing);
            StateCache.Current.ApplyDynamic(Viewport);
            RenderPassOperations.Load(description, Area);
            active = this;
        }
        catch (Exception operationFailure)
        {
            var failures = new List<Exception>();
            try
            {
                try { if (routingCaptured) RestoreRouting(); }
                catch (Exception error) { failures.Add(error); }
                try { bindings.Dispose(); }
                catch (Exception error) { failures.Add(error); }
            }
            finally { targets.Dispose(); active = null; }
            if (failures.Count != 0)
                throw new AggregateException(operationFailure,
                    new EngineBoundaryRestoreException("Failed render pass setup could not restore its target.", new AggregateException(failures)));
            throw;
        }
    }

    /// <summary>Restores only framebuffer-local routing and reports native cleanup failures before handoff.</summary>
    private void RestoreRouting()
    {
        targets.Validate();
        using var errors = new GlDebug.ErrorScope("Render pass routing restoration");
        StateCache.Current.BindFramebuffer(FramebufferTarget.DrawFramebuffer, description.Target.FboId);
        GL.DrawBuffers(previousRouting.Length, previousRouting);
    }
    #endregion
}
