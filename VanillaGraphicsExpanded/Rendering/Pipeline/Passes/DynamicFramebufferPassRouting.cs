using System;
using System.Collections.Generic;
using OpenTK.Graphics.OpenGL;

namespace VanillaGraphicsExpanded.Rendering.Pipeline.Passes;

/// <summary>Restores one standalone dynamic pass's output route and independent framebuffer bindings.</summary>
internal sealed class DynamicFramebufferPassRouting : IDisposable
{
    private readonly DynamicGpuFramebuffer target;
    private readonly StateCache.FramebufferScope bindings;
    private readonly DrawBuffersEnum[] original;
    private bool disposed;

    #region Public API
    /// <summary>Captures mutable routing before applying the pass's complete output selection.</summary>
    internal DynamicFramebufferPassRouting(DynamicGpuFramebuffer target, DrawBuffersEnum[] routing)
    {
        this.target = target;
        bindings = StateCache.Current.BindFramebufferScope();
        try
        {
            target.Bind();
            original = StateCache.Current.CaptureFramebufferDrawRouting(target.FboId);
        }
        catch { bindings.Dispose(); throw; }
        try { StateCache.Current.SetDrawBuffers(routing.Length, routing); }
        catch (Exception operationFailure)
        {
            try { Dispose(); }
            catch (Exception cleanupFailure) { throw new AggregateException(operationFailure, cleanupFailure); }
            throw;
        }
    }
    /// <summary>Attempts routing and binding restoration independently before the pass releases its images.</summary>
    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        var failures = new List<Exception>();
        try
        {
            using var errors = new GlDebug.ErrorScope("Render pass routing restoration");
            target.RestoreDrawRouting(original);
        }
        catch (Exception error) { failures.Add(error); }
        try { bindings.Dispose(); }
        catch (Exception error) { failures.Add(error); }
        if (failures.Count != 0)
            throw new EngineBoundaryRestoreException("Render pass routing cleanup failed.", new AggregateException(failures));
    }
    #endregion
}
