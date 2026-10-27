namespace VanillaGraphicsExpanded.Rendering.RHI;
/// <summary>Defines private native framebuffer-owner handoff, separate from portable attachment-based passes.</summary>
internal partial interface IRHIResourceExecution
{
    #region Framebuffer ownership
    /// <summary>Rejects immutable aliases before owner policy mutates shared attachments.</summary>
    void RequireMutableFramebufferOwner(GpuFramebuffer owner);
    /// <summary>Registers a completed immutable declaration with cleanup independent of the current context/thread.</summary>
    System.IDisposable SealFramebufferConfiguration(GpuFramebuffer owner);
    /// <summary>Requires the original framebuffer owner before selecting its native integration target.</summary>
    void SelectFramebufferOwner(GpuFramebuffer? owner, OpenTK.Graphics.OpenGL.FramebufferTarget target);
    /// <summary>Configures routing under the existing framebuffer owner authority.</summary>
    void ConfigureFramebufferRouting(GpuFramebuffer owner, OpenTK.Graphics.OpenGL.DrawBuffersEnum[] routing, OpenTK.Graphics.OpenGL.ReadBufferMode read);
    /// <summary>Executes one validated owner attachment change and its metadata publication.</summary>
    void ConfigureFramebufferAttachment(GpuFramebuffer owner, OpenTK.Graphics.OpenGL.FramebufferAttachment slot, GpuFramebufferAttachment? image);
    /// <summary>Reconciles changed backing images in one owner transaction.</summary>
    void RefreshFramebufferAttachments(GpuFramebuffer owner, System.Collections.Generic.IEnumerable<System.Collections.Generic.KeyValuePair<OpenTK.Graphics.OpenGL.FramebufferAttachment, GpuFramebufferAttachment>> images);
    /// <summary>Checks a native integration framebuffer without transferring execution to the wrapper.</summary>
    void RequireFramebufferComplete(GpuFramebuffer owner);
    /// <summary>Projects a selected attachment format from its already published image facts.</summary>
    TextureFormat DescribeAttachment(GpuFramebufferAttachment image);
    /// <summary>Selects one owned scratch output and optionally its corresponding read source.</summary>
    void SelectFramebufferColorOutput(GpuFramebuffer owner, int attachment, bool read);
    #endregion
}
