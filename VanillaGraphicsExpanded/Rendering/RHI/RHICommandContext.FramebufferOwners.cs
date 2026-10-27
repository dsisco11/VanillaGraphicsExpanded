namespace VanillaGraphicsExpanded.Rendering.RHI;
/// <summary>Routes existing native integration framebuffer owners through their admitted execution contract.</summary>
public sealed partial class RHICommandContext
{
    #region Internal API
    /// <summary>Admits allocation for an unpublished framebuffer declaration without reentering its constructor.</summary>
    internal void AllocateFramebufferOwner(GpuFramebuffer owner, in GpuFramebufferInitialization request) => backend.AllocateFramebufferOwner(owner, request);
    /// <summary>Admits owner configuration before allocation policy can change shared attachment storage.</summary>
    internal void RequireMutableFramebufferOwner(GpuFramebuffer owner) => backend.RequireMutableFramebufferOwner(owner);
    /// <summary>Seals a completed configuration and returns native-free cleanup for its original context lifetime.</summary>
    internal System.IDisposable SealFramebufferConfiguration(GpuFramebuffer owner) => backend.SealFramebufferConfiguration(owner);
    /// <summary>Requires the original framebuffer owner before selecting its native integration target.</summary>
    internal void SelectFramebufferOwner(GpuFramebuffer? owner, FramebufferSelection target) => backend.SelectFramebufferOwner(owner, target);
    /// <summary>Configures routing under the existing framebuffer owner authority.</summary>
    internal void ConfigureFramebufferRouting(GpuFramebuffer owner, System.ReadOnlySpan<int> routing, int? read) => backend.ConfigureFramebufferRouting(owner, routing, read);
    /// <summary>Executes one validated owner attachment change and its metadata publication.</summary>
    internal void ConfigureFramebufferAttachment(GpuFramebuffer owner, FramebufferSlot slot, GpuFramebufferAttachment? image) => backend.ConfigureFramebufferAttachment(owner, slot, image);
    /// <summary>Reconciles changed backing images in one owner transaction.</summary>
    internal void RefreshFramebufferAttachments(GpuFramebuffer owner) => backend.RefreshFramebufferAttachments(owner);
    /// <summary>Checks a native integration framebuffer without transferring execution to the wrapper.</summary>
    internal void RequireFramebufferComplete(GpuFramebuffer owner) => backend.RequireFramebufferComplete(owner);
    /// <summary>Selects the existing scratch framebuffer output through its execution owner.</summary>
    internal void SelectFramebufferColorOutput(GpuFramebuffer owner, int attachment, bool read = false) => backend.SelectFramebufferColorOutput(owner, attachment, read);
    #endregion
}
