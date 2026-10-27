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
    public void SelectFramebufferOwner(GpuFramebuffer? owner, FramebufferTarget target)
    {
        RequireContext();
        if (owner is null) OpenGLFramebufferSelection.Bind(cache, target, 0);
        else { owner.RequireOriginalContext(); OpenGLFramebufferConfiguration.BindFramebufferResource(owner, target); }
    }
    /// <inheritdoc />
    public void ConfigureFramebufferRouting(GpuFramebuffer owner, DrawBuffersEnum[] routing, ReadBufferMode read)
    { RequireContext(); OpenGLFramebufferConfiguration.ConfigureRouting(owner, routing, read); }
    /// <inheritdoc />
    public void ConfigureFramebufferAttachment(GpuFramebuffer owner, FramebufferAttachment slot, GpuFramebufferAttachment? image)
    { RequireMutableFramebufferOwner(owner); OpenGLFramebufferConfiguration.FramebufferTexture(owner, slot, image); }
    /// <inheritdoc />
    public void RefreshFramebufferAttachments(GpuFramebuffer owner, System.Collections.Generic.IEnumerable<System.Collections.Generic.KeyValuePair<FramebufferAttachment, GpuFramebufferAttachment>> images)
    { RequireMutableFramebufferOwner(owner); OpenGLFramebufferConfiguration.ReattachFramebuffer(owner, images); }
    /// <inheritdoc />
    public void RequireFramebufferComplete(GpuFramebuffer owner)
    { RequireContext(); owner.RequireOriginalContext(); OpenGLFramebufferCompleteness.RequireComplete(owner.FboId); }
    /// <inheritdoc />
    public TextureFormat DescribeAttachment(GpuFramebufferAttachment image)
    { RequireContext(); return OpenGLImageValues.Format(image.InternalFormat); }
    /// <inheritdoc />
    public void SelectFramebufferColorOutput(GpuFramebuffer owner, int attachment, bool read)
    {
        RequireContext(); owner.RequireOriginalContext();
        if ((uint)attachment >= (uint)Capabilities.MaxColorOutputs) throw new System.ArgumentOutOfRangeException(nameof(attachment));
        OpenGLFramebufferRouting.SetDrawBuffer(cache, DrawBufferMode.ColorAttachment0 + attachment);
        if (read) OpenTK.Graphics.OpenGL4.GL.NamedFramebufferReadBuffer(owner.FboId,
            OpenTK.Graphics.OpenGL4.ReadBufferMode.ColorAttachment0 + attachment);
        RequireContext();
    }
    #endregion
}
