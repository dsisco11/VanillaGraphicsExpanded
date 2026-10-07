using System;
using System.Collections.Generic;
using System.Linq;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Rendering.Pipeline.Passes;
using Vintagestory.API.Client;

namespace VanillaGraphicsExpanded.DebugView;

/// <summary>Publishes borrowed debug destinations at engine target changes and window resize boundaries.</summary>
internal sealed class DebugRenderTarget : IDisposable
{
    private readonly ICoreClientAPI api;
    private readonly Action unregister;
    private readonly Dictionary<int, GpuFramebuffer> targets = new();
    private bool dirty = true;
    private int windowWidth, windowHeight;

    #region Public API
    /// <summary>Registers invalidation after engine framebuffer recreation without allocating a render target.</summary>
    internal DebugRenderTarget(ICoreClientAPI api)
    {
        this.api = api;
        unregister = ScreenResourceManager.Register(ScreenResourceManager.CompositeOrder, Invalidate);
        api.Event.LeaveWorld += Invalidate;
    }

    /// <summary>Resolves the actual bound draw target, retaining exact published formats across unchanged debug draws.</summary>
    internal RenderPassDesc GetPass(int colorCount = 1)
    {
        int id = StateCache.Current.GetCurrentFramebuffer(FramebufferTarget.DrawFramebuffer);
        if (dirty || windowWidth != api.Render.FrameWidth || windowHeight != api.Render.FrameHeight)
        {
            // Keep separate publications across OIT and window draws. Alternating stable
            // targets must not turn cold metadata queries into work on every frame.
            ReleaseTargets();
            windowWidth = api.Render.FrameWidth; windowHeight = api.Render.FrameHeight;
            dirty = false;
        }
        if (!targets.TryGetValue(id, out var target))
        {
            // Native names may be reused during a rebuild, so explicit publication invalidation takes precedence.
            var replacement = GpuFramebuffer.Wrap(id, "Debug.Destination", api.Render.FrameWidth, api.Render.FrameHeight);
            try
            {
                if (id == 0) replacement.PublishSurfaceMetadata(api.Render.FrameWidth, api.Render.FrameHeight);
                else replacement.PublishRenderPassMetadata();
            }
            catch { replacement.Dispose(); throw; }
            targets[id] = target = replacement;
        }
        return target.Surface is { } surface
            ? new(target, [new(-1, SurfaceBuffer: surface.Buffer)])
            : new(target, Enumerable.Range(0, colorCount).Select(i => new RenderPassColor(i)).ToArray());
    }

    /// <summary>Releases wrappers and callbacks while preserving externally owned images and the window.</summary>
    public void Dispose()
    {
        unregister();
        api.Event.LeaveWorld -= Invalidate;
        ReleaseTargets();
    }
    #endregion

    #region Private
    /// <summary>Retires metadata at destination lifecycle boundaries without deleting engine storage.</summary>
    private void ReleaseTargets()
    {
        foreach (var target in targets.Values) target.Dispose();
        targets.Clear();
    }

    /// <summary>Forces metadata renewal even if a rebuilt engine framebuffer reuses its old integer name.</summary>
    private void Invalidate() => dirty = true;
    #endregion
}
