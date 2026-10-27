namespace VanillaGraphicsExpanded.Rendering.RHI;
/// <summary>Routes existing native integration framebuffer owners through their admitted execution contract.</summary>
public sealed partial class RHICommandContext
{
    #region Internal API
    /// <summary>Requires the original framebuffer owner before selecting its native integration target.</summary>
    internal void SelectFramebufferOwner(GpuFramebuffer? owner, OpenTK.Graphics.OpenGL.FramebufferTarget target) => backend.SelectFramebufferOwner(owner, target);
    /// <summary>Configures routing under the existing framebuffer owner authority.</summary>
    internal void ConfigureFramebufferRouting(GpuFramebuffer owner, OpenTK.Graphics.OpenGL.DrawBuffersEnum[] routing, OpenTK.Graphics.OpenGL.ReadBufferMode read) => backend.ConfigureFramebufferRouting(owner, routing, read);
    /// <summary>Executes one validated owner attachment change and its metadata publication.</summary>
    internal void ConfigureFramebufferAttachment(GpuFramebuffer owner, OpenTK.Graphics.OpenGL.FramebufferAttachment slot, GpuFramebufferAttachment? image) => backend.ConfigureFramebufferAttachment(owner, slot, image);
    /// <summary>Reconciles changed backing images in one owner transaction.</summary>
    internal void RefreshFramebufferAttachments(GpuFramebuffer owner, System.Collections.Generic.IEnumerable<System.Collections.Generic.KeyValuePair<OpenTK.Graphics.OpenGL.FramebufferAttachment, GpuFramebufferAttachment>> images) => backend.RefreshFramebufferAttachments(owner, images);
    /// <summary>Checks a native integration framebuffer without transferring execution to the wrapper.</summary>
    internal void RequireFramebufferComplete(GpuFramebuffer owner) => backend.RequireFramebufferComplete(owner);
    /// <summary>Projects a selected attachment format without native acquisition.</summary>
    internal TextureFormat DescribeAttachment(GpuFramebufferAttachment image) => backend.DescribeAttachment(image);
    /// <summary>Selects the existing scratch framebuffer output through its execution owner.</summary>
    internal void SelectFramebufferColorOutput(GpuFramebuffer owner, int attachment, bool read = false) => backend.SelectFramebufferColorOutput(owner, attachment, read);
    #endregion
}
