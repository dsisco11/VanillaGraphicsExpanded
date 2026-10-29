using VanillaGraphicsExpanded.Rendering.Integration.OpenGL;
using OpenTK.Graphics.OpenGL;
namespace VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;
/// <summary>Executes native integration framebuffer-owner requests with original-context admission.</summary>
internal sealed partial class OpenGLBackend
{
    #region Public API
    /// <inheritdoc />
    public void AllocateFramebufferOwner(GpuFramebuffer owner, in GpuFramebufferInitialization request)
    {
        RequireContext();
        if (!GpuResourceManager.CreateFramebuffer(request, owner, out _).HasValue)
            throw new System.InvalidOperationException("Framebuffer allocation failed.");
    }
    /// <inheritdoc />
    public void RequireMutableFramebufferOwner(GpuFramebuffer owner)
    {
        RequireContext();
        owner.RequireOriginalContext();
        cache.RequireMutableFramebufferConfiguration(owner.FboId);
        owner.RequireMutableStorage();
    }
    /// <inheritdoc />
    public System.IDisposable SealFramebufferConfiguration(GpuFramebuffer owner)
    {
        RequireContext();
        owner.RequireOriginalContext();
        return new OpenGLFramebufferConfigurationLease(cache, owner);
    }
    /// <inheritdoc />
    public void SelectFramebufferOwner(GpuFramebuffer? owner, FramebufferSelection target)
    {
        RequireContext();
        if (owner is null) OpenGLFramebufferSelection.Bind(cache, OpenGLResourceValues.Native(target), 0);
        else { owner.RequireOriginalContext(); OpenGLFramebufferConfiguration.BindFramebufferResource(owner, OpenGLResourceValues.Native(target)); }
    }
    /// <inheritdoc />
    public void ConfigureFramebufferRouting(GpuFramebuffer owner, System.ReadOnlySpan<int> routing, int? read)
    {
        RequireContext();
        var native = new DrawBuffersEnum[routing.Length];
        for (int i = 0; i < native.Length; i++)
            native[i] = routing[i] == -1 ? DrawBuffersEnum.None : (uint)routing[i] < 32
                ? DrawBuffersEnum.ColorAttachment0 + routing[i] : throw new System.ArgumentOutOfRangeException(nameof(routing));
        if (read.HasValue && (uint)read.Value >= 32) throw new System.ArgumentOutOfRangeException(nameof(read));
        OpenGLFramebufferConfiguration.ConfigureRouting(owner, native, read.HasValue ? ReadBufferMode.ColorAttachment0 + read.Value : ReadBufferMode.None);
    }
    /// <inheritdoc />
    public void ConfigureFramebufferAttachment(GpuFramebuffer owner, FramebufferSlot slot, GpuFramebufferAttachment? image)
    { RequireMutableFramebufferOwner(owner); OpenGLFramebufferConfiguration.FramebufferTexture(owner, slot, image); }
    /// <inheritdoc />
    public void RefreshFramebufferAttachments(GpuFramebuffer owner)
    { RequireMutableFramebufferOwner(owner); OpenGLFramebufferConfiguration.ReattachFramebuffer(owner, owner.AttachmentImages); }
    /// <inheritdoc />
    public void RequireFramebufferComplete(GpuFramebuffer owner)
    { RequireContext(); owner.RequireOriginalContext(); OpenGLFramebufferCompleteness.RequireComplete(owner.FboId); }
    /// <inheritdoc />
    public void SelectFramebufferColorOutput(GpuFramebuffer owner, int attachment, bool read)
    {
        RequireContext(); owner.RequireOriginalContext();
        if ((uint)attachment >= (uint)Capabilities.MaxColorOutputs) throw new System.ArgumentOutOfRangeException(nameof(attachment));
        OpenGLFramebufferRouting.SetDrawBuffer(cache, DrawBufferMode.ColorAttachment0 + attachment);
        if (read) OpenGLDriverCalls.NamedFramebufferReadBuffer(owner.FboId,
            OpenTK.Graphics.OpenGL4.ReadBufferMode.ColorAttachment0 + attachment);
        RequireContext();
        if (read) cache.RememberFramebufferReadRouting(owner.FboId, ReadBufferMode.ColorAttachment0 + attachment);
    }
    #endregion
}
