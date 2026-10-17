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
    private readonly StateCache.FramebufferScope? bindings;
    private readonly DrawBuffersEnum[]? previousRouting;
    private bool disposed;
    internal RenderTargetSignature Signature => targets.Signature;
    internal RenderArea Area => targets.Area;
    internal DynamicDrawState Viewport => new() { X = Area.X, Y = Area.Y, Width = Area.Width, Height = Area.Height };

    #region Public API
    /// <summary>Rejects typed view sampling when its exposed storage overlaps any active framebuffer image.</summary>
    internal static void ValidateSampledView(GpuTextureView view)
    {
        if (active is null) return;
        foreach (var pair in active.description.Target.AttachmentImages)
        {
            var image = pair.Value;
            var attachedView = image.Resource as GpuTextureView;
            int source = attachedView?.SourceTextureId ?? image.TextureId;
            if (source != view.SourceTextureId) continue;
            int level = image.MipLevel + (attachedView?.BaseLevel ?? 0);
            if (level < view.BaseLevel || level >= (long)view.BaseLevel + view.LevelCount) continue;
            // A 3D view exposes one volume, not one z slice. Sampling it can reach every slice
            // of the selected mip even though GL reports a view layer count of one.
            if (view.Target == TextureTarget.Texture3D)
                throw new InvalidOperationException("Sampled volume view overlaps an active framebuffer image.");
            int first = (attachedView?.BaseLayer ?? 0) + (image.Layer ?? 0);
            int count = image.Layer.HasValue ? 1 : attachedView?.LayerCount ?? int.MaxValue;
            if (first < (long)view.BaseLayer + view.LayerCount && view.BaseLayer < (long)first + count)
                throw new InvalidOperationException("Sampled texture view overlaps an active framebuffer image.");
        }
    }

    /// <summary>Validates metadata and intentions before setup; optional sequence ownership defers routing restoration.</summary>
    internal static RenderPass Begin(RenderPassDesc description, EngineBoundaryScope boundary, RenderPassSequence? sequence = null)
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
    internal void ValidatePipeline(GraphicsPipeline pipeline)
    {
        Validate();
        pipeline.ValidateTargets(Signature);
    }

    /// <summary>Ends image authority; standalone passes also restore routing and bindings immediately.</summary>
    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        var failures = new List<Exception>();
        try
        {
            // Store discard permits undefined contents. Retaining them is a legal conservative
            // implementation, including partial areas and independently preserved packed aspects.
            try { if (previousRouting is not null) RestoreRouting(); }
            catch (Exception error) { failures.Add(error); }
            try { bindings?.Dispose(); }
            catch (Exception error) { failures.Add(error); }
        }
        finally { targets.Dispose(); active = null; }
        if (failures.Count != 0) throw new EngineBoundaryRestoreException("Render pass cleanup failed.", new AggregateException(failures));
    }
    #endregion

    #region Private
    /// <summary>Acquires resources first, then changes routing and applies validated load operations.</summary>
    private RenderPass(RenderPassDesc description, EngineBoundaryScope boundary, RenderPassSequence? sequence)
    {
        this.description = description; this.boundary = boundary;
        targets = new(description);
        try { if (sequence is null) bindings = StateCache.Current.BindFramebufferScope(); }
        catch { targets.Dispose(); throw; }
        if (sequence is null) previousRouting = new DrawBuffersEnum[GpuSupport.Graphics.MaxDrawBuffers];
        bool routingCaptured = false;
        try
        {
            using var errors = new GlDebug.ErrorScope("Render pass setup");
            var routing = new DrawBuffersEnum[Math.Max(1, description.Colors.Count)];
            for (int i = 0; i < description.Colors.Count; i++)
                routing[i] = description.Colors[i].SurfaceBuffer ?? (description.Colors[i].Attachment < 0 ? DrawBuffersEnum.None
                    : DrawBuffersEnum.ColorAttachment0 + description.Colors[i].Attachment);
            if (sequence is not null) sequence.Bind(description.Target, routing);
            else
            {
                description.Target.Bind();
                for (int i = 0; i < previousRouting!.Length; i++)
                    previousRouting[i] = (DrawBuffersEnum)GL.GetInteger(GetPName.DrawBuffer0 + i);
                GlDebug.ThrowIfErrors("Render pass routing capture");
                routingCaptured = true;
                GL.DrawBuffers(routing.Length, routing);
            }
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
                try { bindings?.Dispose(); }
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
        description.Target.RestoreDrawRouting(previousRouting!);
    }
    #endregion
}
