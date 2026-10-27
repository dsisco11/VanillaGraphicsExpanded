using System.Collections.Generic;
using OpenTK.Graphics.OpenGL;
namespace VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;
/// <summary>Owns attachment, routing and blend execution for explicitly engine-owned targets.</summary>
internal sealed partial class OpenGLBackend
{
    #region Internal API
    /// <summary>Queries external target completeness without disturbing its selection.</summary>
    internal bool IsExternalFramebufferComplete(int framebuffer)
    {
        RequireContext();
        var status = OpenTK.Graphics.OpenGL4.GL.CheckNamedFramebufferStatus(framebuffer, OpenTK.Graphics.OpenGL4.FramebufferTarget.Framebuffer);
        RequireContext();
        return status == OpenTK.Graphics.OpenGL4.FramebufferStatus.FramebufferComplete;
    }
    /// <summary>Publishes a complete attachment set through the existing manager operation once.</summary>
    internal bool ConfigureEngineFramebuffer(int framebuffer, IReadOnlyDictionary<FramebufferSlot, GpuFramebufferAttachment?> attachments)
    {
        RequireContext();
        using var target = DynamicGpuFramebuffer.Wrap(framebuffer);
        // Validate the complete supplied image set before issuing the first native attachment command.
        foreach (var pair in attachments) pair.Value?.ValidateSlot(pair.Key);
        using var operation = new OpenGLFramebufferConfiguration.FramebufferOperation(target);
        foreach (var pair in attachments) OpenGLFramebufferConfiguration.FramebufferTexture(target, pair.Key, pair.Value, operation);
        operation.Commit();
        // Publication observers may retire the original association; never inspect a reused name.
        if (!target.IsValid) return false;
        return IsExternalFramebufferComplete(framebuffer);
    }
    /// <summary>Updates only the engine route and explicitly requested blend state.</summary>
    internal void RouteEngineFramebuffer(DrawBuffersEnum[] route, GlPipelineDesc? blend, bool forceDirty)
    {
        RequireContext();
        OpenGLFramebufferRouting.SetDrawBuffers(cache, route.Length, route);
        if (blend is not null)
        {
            ApplyEngineFramebufferBlend(blend.Value, forceDirty);
        }
    }
    /// <summary>Establishes indexed engine state without changing framebuffer selection.</summary>
    internal void ApplyEngineFramebufferBlend(GlPipelineDesc blend, bool forceDirty)
    {
        RequireContext();
        if (forceDirty) { cache.DirtyIndexedBlendFunc(); cache.DirtyIndexedBlendEnable(); }
        cache.Apply(blend);
    }
    /// <summary>Restores indexed engine policy after a global blend edit without switching framebuffers.</summary>
    internal void ReapplyEngineFramebufferBlend(int framebuffer, GlPipelineDesc blend)
    {
        RequireContext();
        if (cache.GetCurrentFramebuffer(FramebufferTarget.Framebuffer) != framebuffer) return;
        ApplyEngineFramebufferBlend(blend, true);
    }
    /// <summary>Removes only declared extension attachments and restores the engine teardown route.</summary>
    internal void DetachEngineFramebuffer(int framebuffer, FramebufferSlot[] attachments, DrawBuffersEnum[] route)
    {
        RequireContext();
        var changes = new Dictionary<FramebufferSlot, GpuFramebufferAttachment?>();
        foreach (var slot in attachments) changes.Add(slot, null);
        ConfigureEngineFramebuffer(framebuffer, changes);
        OpenGLFramebufferSelection.Bind(cache, FramebufferTarget.Framebuffer, framebuffer);
        RouteEngineFramebuffer(route, null, false);
        OpenGLFramebufferSelection.Bind(cache, FramebufferTarget.Framebuffer, 0);
    }
    #endregion
}
