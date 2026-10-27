using System;
using System.Collections.Generic;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;

namespace VanillaGraphicsExpanded.Rendering.Pipeline.Passes;

/// <summary>Preserves only externally owned framebuffer routing touched by one admitted pass sequence.</summary>
internal sealed class RenderPassSequence : IDisposable
{
    private readonly EngineBoundaryScope boundary;
    private readonly OpenGLBackend backend;
    private readonly Dictionary<int, Routing> external = new();
    private bool disposed;

    #region Public API
    /// <summary>Restores independent external routes once, before the boundary restores its original framebuffer selectors.</summary>
    public void Dispose()
    {
        if(disposed)return;
        disposed=true;
        var failures=new List<Exception>();
        foreach(var route in external.Values)
        {
            try { route.Restore(); }
            catch(Exception error){failures.Add(error);}
            finally {route.Release();}
        }
        external.Clear();
        if(failures.Count!=0)throw new EngineBoundaryRestoreException("Pass routing restoration failed.",new AggregateException(failures));
    }
    #endregion

    #region Internal API
    /// <summary>Registers specialized restoration before individual passes so their attachment guards release first.</summary>
    internal RenderPassSequence(EngineBoundaryScope boundary,OpenGLBackend backend)
    {
        this.boundary=boundary;this.backend=backend;
        boundary.AddCleanup(EngineBoundaryCleanup.Framebuffers,this,Abandon);
    }
    /// <summary>Selects a realized immutable target or captures external routing before its first admitted mutation.</summary>
    internal void Bind(GpuFramebuffer target,DrawBuffersEnum[] outputs)
    {
        ObjectDisposedException.ThrowIf(disposed,this);
        StateCache.Current.RequireEngineBoundary(boundary);
        if(target is OpenGLPassTarget realized)
        {
            if(!realized.IsReusable)throw new InvalidOperationException("The pass target realization is obsolete.");
            realized.ValidateDeclaredRouting(outputs);
            backend.SelectPassTarget(realized);
            return;
        }
        if(target.OwnsNativeFramebuffer)throw new InvalidOperationException("Portable passes cannot borrow renderer-owned framebuffer execution.");
        backend.SelectPassTarget(target);
        if(!external.TryGetValue(target.FboId,out var routing))
        {
            routing=new(target,backend);external.Add(target.FboId,routing);
        }
        routing.Apply(outputs);
    }
    #endregion

    #region Private
    /// <summary>Releases CPU lifetime guards on irreversible context loss without issuing restoration commands.</summary>
    private void Abandon()
    {
        if(disposed)return;disposed=true;
        foreach(var route in external.Values)route.Release();
        external.Clear();
    }

    /// <summary>Retains one original external allocation and its pre-mutation route, rather than an asset owner or a reusable native-name lookup.</summary>
    private sealed class Routing
    {
        private readonly GpuFramebuffer target;
        private readonly OpenGLBackend backend;
        private readonly DrawBuffersEnum[] original;
        private bool applied;

        #region Internal API
        /// <summary>Captures routing only after the already guarded original target has been selected.</summary>
        internal Routing(GpuFramebuffer target,OpenGLBackend backend)
        {
            this.target=target;this.backend=backend;
            target.RetainLifetimeReference();
            try {original=OpenGLFramebufferRouting.Capture(StateCache.Current,target.FboId);}
            catch {target.ReleaseLifetimeReference();throw;}
        }
        /// <summary>Remembers attempted mutation so failed native publication still participates in restoration.</summary>
        internal void Apply(DrawBuffersEnum[] outputs)
        {
            applied=true;backend.RequireContext();
            OpenGLFramebufferRouting.SetDrawBuffers(StateCache.Current,outputs.Length,outputs);
        }
        /// <summary>Restores through named commands; only the default window requires selecting its draw alias.</summary>
        internal void Restore()
        {
            if(!applied)return;
            backend.RequireContext();
            if(target.FboId==0)backend.SelectPassTarget(target);
            OpenGLFramebufferRouting.Restore(StateCache.Current,target.FboId,original);
        }
        /// <summary>Releases the original allocation's lifetime guard after restoration or abandonment.</summary>
        internal void Release()=>target.ReleaseLifetimeReference();
        #endregion
    }
    #endregion
}
