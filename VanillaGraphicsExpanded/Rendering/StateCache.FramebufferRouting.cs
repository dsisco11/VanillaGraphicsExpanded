using System;
using System.Collections.Generic;
using OpenTK.Graphics.OpenGL;

namespace VanillaGraphicsExpanded.Rendering;

/// <summary>Owns complete framebuffer-local draw routes independently of framebuffer selection.</summary>
internal sealed partial class StateCache
{
    private readonly Dictionary<int, DrawBuffersEnum[]> framebufferDrawRouting = new();
    /// <summary>Counts issued bound, named and single-selector routing operations.</summary>
    internal long FramebufferRoutingCalls { get; private set; }
    /// <summary>Counts native output-slot queries needed to resolve unknown routing.</summary>
    internal long FramebufferRoutingQueries { get; private set; }

    #region Public API
    #region Publication
    /// <summary>Publishes the current draw framebuffer's complete output mapping, disabling unspecified trailing slots.</summary>
    internal void SetDrawBuffers(int count, DrawBuffersEnum[] buffers)
    {
        var route = NormalizeDrawRouting(count, buffers);
        ApplyFramebufferDrawRouting(GetCurrentFramebuffer(FramebufferTarget.DrawFramebuffer), route, count, false, false);
    }

    /// <summary>Publishes a single selector, preserving grouped default-window selectors such as Back.</summary>
    internal void SetDrawBuffer(DrawBufferMode buffer)
    {
        var route = NormalizeDrawRouting(1, [(DrawBuffersEnum)buffer]);
        ApplyFramebufferDrawRouting(GetCurrentFramebuffer(FramebufferTarget.DrawFramebuffer), route, 1, true, false);
    }

    /// <summary>Restores an explicit target without binding it when unchanged or when named operations are available.</summary>
    internal void RestoreFramebufferDrawRouting(int framebuffer, DrawBuffersEnum[] buffers)
    {
        ArgumentNullException.ThrowIfNull(buffers);
        if (framebuffer < 0) throw new ArgumentOutOfRangeException(nameof(framebuffer));
        var route = NormalizeDrawRouting(buffers.Length, buffers);
        bool single = framebuffer == 0;
        for (int i = 1; i < route.Length; i++) single &= route[i] == DrawBuffersEnum.None;
        ApplyFramebufferDrawRouting(framebuffer, route, single ? 1 : buffers.Length, single, true);
    }
    #endregion

    #region Capture and invalidation
    /// <summary>Returns an independent copy of the bound target's route, querying only unknown framebuffer identities.</summary>
    internal DrawBuffersEnum[] CaptureFramebufferDrawRouting(int framebuffer)
    {
        if (GetCurrentFramebuffer(FramebufferTarget.DrawFramebuffer) != framebuffer)
            throw new InvalidOperationException("Routing capture requires the selected draw framebuffer.");
        if (staticFramebuffers.TryGetValue(framebuffer, out var immutable)) return immutable.CaptureRouting();
        if (!framebufferDrawRouting.TryGetValue(framebuffer, out var route))
        {
            route = new DrawBuffersEnum[GpuSupport.Graphics.MaxDrawBuffers];
            using (var errors = new GlDebug.ErrorScope("Framebuffer routing capture"))
                for (int i = 0; i < route.Length; i++)
                {
                    route[i] = (DrawBuffersEnum)GL.GetInteger(GetPName.DrawBuffer0 + i);
                    FramebufferRoutingQueries++;
                }
            framebufferDrawRouting.Add(framebuffer, route);
        }
        return (DrawBuffersEnum[])route.Clone();
    }

    /// <summary>Forgets a retired, transferred or externally republished framebuffer identity without native mutation.</summary>
    internal void InvalidateFramebufferDrawRouting(int framebuffer) => framebufferDrawRouting.Remove(framebuffer);
    #endregion
    #endregion

    #region Private
    /// <summary>Copies caller storage and pads every unspecified output with None for complete comparisons.</summary>
    private static DrawBuffersEnum[] NormalizeDrawRouting(int count, DrawBuffersEnum[] buffers)
    {
        ArgumentNullException.ThrowIfNull(buffers);
        int limit = GpuSupport.Graphics.MaxDrawBuffers;
        if (count < 0 || count > buffers.Length || count > limit) throw new ArgumentOutOfRangeException(nameof(count));
        var route = new DrawBuffersEnum[limit];
        buffers.AsSpan(0, count).CopyTo(route);
        return route;
    }

    /// <summary>Changes only unknown or different mappings and publishes knowledge after checked native success.</summary>
    private void ApplyFramebufferDrawRouting(int framebuffer, DrawBuffersEnum[] route, int count, bool single, bool restore)
    {
        if (staticFramebuffers.TryGetValue(framebuffer, out var immutable))
        {
            immutable.ValidatePassRouting(route);
            return;
        }
        if (framebufferDrawRouting.TryGetValue(framebuffer, out var previous) && previous.AsSpan().SequenceEqual(route)) return;
        // A rejected native operation must not leave either the old or attempted mapping trusted.
        framebufferDrawRouting.Remove(framebuffer);
        using (var errors = new GlDebug.ErrorScope("Framebuffer routing publication"))
        {
            if (restore && framebuffer != 0 && GpuSupport.Graphics.SupportsArbDirectStateAccess)
                GL.NamedFramebufferDrawBuffers(framebuffer, count, route);
            else
            {
                if (restore) BindFramebuffer(FramebufferTarget.DrawFramebuffer, framebuffer);
                if (single) GL.DrawBuffer((DrawBufferMode)route[0]);
                else GL.DrawBuffers(count, route);
            }
            FramebufferRoutingCalls++;
        }
        framebufferDrawRouting[framebuffer] = route;
    }
    #endregion
}
