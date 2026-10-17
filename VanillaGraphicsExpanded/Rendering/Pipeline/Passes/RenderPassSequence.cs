using System;
using System.Collections.Generic;
using OpenTK.Graphics.OpenGL;

namespace VanillaGraphicsExpanded.Rendering.Pipeline.Passes;

/// <summary>Retains framebuffer-local routing until an owned graphics sequence returns to the engine.</summary>
internal sealed class RenderPassSequence : IDisposable
{
    private readonly EngineBoundaryScope boundary;
    private readonly Dictionary<int, Routing> targets = new();
    private bool disposed;

    #region Public API
    /// <summary>Registers routing restoration before individual passes so active passes release first during cleanup.</summary>
    internal RenderPassSequence(EngineBoundaryScope boundary)
    {
        this.boundary = boundary;
        boundary.AddCleanup(EngineBoundaryCleanup.Framebuffers, this, Abandon);
    }

    /// <summary>Binds only the draw target and publishes a route without restoring an intermediate engine binding.</summary>
    internal void Bind(GpuFramebuffer target, DrawBuffersEnum[] routing)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        StateCache.Current.RequireEngineBoundary(boundary);
        target.Bind(FramebufferTarget.DrawFramebuffer);
        if (!targets.TryGetValue(target.FboId, out var state))
        {
            // Routing belongs to the framebuffer, not to a pass. Capture it once even if a target is revisited.
            state = new Routing(target);
            targets.Add(target.FboId, state);
        }
        state.Apply(routing);
    }

    /// <summary>Restores every touched target's original routing while the outer boundary still owns framebuffer bindings.</summary>
    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        var failures = new List<Exception>();
        foreach (var state in targets.Values)
        {
            try { state.Restore(); }
            catch (Exception failure) { failures.Add(failure); }
            finally { state.Release(); }
        }
        targets.Clear();
        if (failures.Count != 0)
            throw new EngineBoundaryRestoreException("Graphics sequence routing restoration failed.", new AggregateException(failures));
    }
    #endregion

    #region Private
    /// <summary>Releases CPU ownership after context loss without issuing calls into the retired native context.</summary>
    private void Abandon()
    {
        if (disposed) return;
        disposed = true;
        foreach (var state in targets.Values) state.Release();
        targets.Clear();
    }

    /// <summary>Owns one framebuffer lifetime borrow and its original output route.</summary>
    private sealed class Routing
    {
        private readonly GpuFramebuffer target;
        private readonly DrawBuffersEnum[] original;
        private bool applied;

        #region Public API
        /// <summary>Captures the bound draw framebuffer before its first routing mutation.</summary>
        internal Routing(GpuFramebuffer target)
        {
            this.target = target;
            target.RetainLifetimeReference();
            try
            {
                original = new DrawBuffersEnum[GpuSupport.Graphics.MaxDrawBuffers];
                for (int i = 0; i < original.Length; i++)
                    original[i] = (DrawBuffersEnum)GL.GetInteger(GetPName.DrawBuffer0 + i);
                GlDebug.ThrowIfErrors("Graphics sequence routing capture");
            }
            catch { target.ReleaseLifetimeReference(); throw; }
        }

        /// <summary>Publishes each pass's complete route, including disabled trailing slots.</summary>
        internal void Apply(DrawBuffersEnum[] routing)
        {
            // Remember the attempted mutation first so exceptional setup still restores the original route.
            applied = true;
            GL.DrawBuffers(routing.Length, routing);
        }

        /// <summary>Restores framebuffer-local state without altering the read framebuffer binding.</summary>
        internal void Restore()
        {
            if (!applied) return;
            using var errors = new GlDebug.ErrorScope("Graphics sequence routing restoration");
            StateCache.Current.BindFramebuffer(FramebufferTarget.DrawFramebuffer, target.FboId);
            // Window surfaces may publish grouped selectors such as Back, which DrawBuffers rejects.
            if (target.FboId == 0) GL.DrawBuffer((DrawBufferMode)original[0]);
            else GL.DrawBuffers(original.Length, original);
        }

        /// <summary>Ends the routing borrow after native restoration or context abandonment.</summary>
        internal void Release() => target.ReleaseLifetimeReference();
        #endregion

    }
    #endregion
}
