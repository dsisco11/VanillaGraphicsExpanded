using VanillaGraphicsExpanded.Rendering.Integration.OpenGL;
using System;
using OpenTK.Graphics.OpenGL;

namespace VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;

/// <summary>Observes framebuffer descriptors while preserving split native bindings and retained attachment ownership.</summary>
internal sealed partial class OpenGlStateCache
{
    #region Private
    /// <summary>Publishes one completed framebuffer mutation through the manager's existing metadata authority.</summary>
    private static void ObserveFramebufferMutation(int selector, bool named, int slot = 0, int texture = 0,
        int level = 0, int layer = -1, int family = 3, FramebufferDefaultParameter? parameter = null)
    {
        var manager = GpuResourceManagerSystem.CurrentManager;
        if (manager?.CanObserveNativeMetadata != true && Current.resourceOperation is null) return;
        if (!named && (FramebufferTarget)selector is not (FramebufferTarget.Framebuffer or FramebufferTarget.DrawFramebuffer or FramebufferTarget.ReadFramebuffer)) return;
        var limits = GpuSupport.Graphics;
        bool validSlot = slot is (int)FramebufferAttachment.DepthAttachment or (int)FramebufferAttachment.StencilAttachment
            or (int)FramebufferAttachment.DepthStencilAttachment
            || slot >= (int)FramebufferAttachment.ColorAttachment0
                && (long)slot - (int)FramebufferAttachment.ColorAttachment0 < limits.MaxColorAttachments;
        if (!parameter.HasValue && !validSlot) return;
        int id = named ? selector : Current.GetCurrentFramebuffer((FramebufferTarget)selector);
        Current.InterruptNativeResourceOperation(GpuResourceKind.Framebuffer, id);
        if (manager?.CanObserveNativeMetadata != true) return;
        var entry = manager.FindObservedResource(GpuResourceKind.Framebuffer, id) as GpuResourceMetadataEntry<GpuFramebufferMetadata>;
        if (entry is null) return;
        entry.Reader ??= GpuFramebufferMetadataReader.Instance;
        if (InterruptNativeMetadataCompletion(entry, GpuMetadataChange.Metadata)) return;
        bool known = entry.IsUsable;
        entry.OperationInProgress = true;
        entry.BeginRefresh(GpuMetadataChange.Metadata);
        try
        {
            if (parameter.HasValue) { PublishObservedFramebuffer(entry, known, parameter: parameter); return; }
            bool rejected = !validSlot || level < 0
                || family == 1 && (layer < 0 || layer >= limits.MaxArrayTextureLayers);
            bool imageExists = false;
            var source = entry.Manager.FindObservedResource(GpuResourceKind.Texture, texture)
                as GpuResourceMetadataEntry<GpuTextureMetadata>;
            if (source?.IsUsable == true && source.Metadata.Target == TextureDimension.TwoArray)
                foreach (var image in source.Metadata.Levels)
                    if (image.Level == level && image.Allocated)
                    {
                        imageExists = true;
                        break;
                    }
            if (texture == 0) { rejected = !validSlot; imageExists = true; }

            bool eligible = family is 0 or 1 && !rejected && imageExists;
            PublishObservedFramebuffer(entry, known, new(OpenGLResourceValues.FromNative((FramebufferAttachment)slot), texture, level, family == 1 ? layer : -1,
                0, 0, GpuFramebufferAttachmentMode.Ordinary, eligible, rejected));
        }
        finally
        {
            // Argument classification failure must not leave a registration in an active operation.
            entry.OperationInProgress = false;
            // Dependency helpers must also lose stale observations if classification or acquisition failed.
            if (entry.Manager.CanObserveNativeMetadata) entry.NotifyFramebufferDependents();
        }
    }

    /// <summary>Applies proven effects directly and limits acquisition to unresolved framebuffer fields.</summary>
    private static void PublishObservedFramebuffer(GpuResourceMetadataEntry<GpuFramebufferMetadata> entry,
        bool known, FramebufferAttachmentCommand? command = null, FramebufferDefaultParameter? parameter = null, bool provenRejected = false)
    {
        entry.OperationInProgress = false;
        if (!entry.Manager.IsRegistered(entry) || entry.State != GpuResourceRegistrationState.Live) return;
        if (!entry.Manager.CanObserveNativeMetadata)
            throw new InvalidOperationException("Framebuffer metadata completion requires its originating context.");
        if (known && (provenRejected || command is { Rejected: true }))
        {
            // Rejected attachment commands have no effect, so retained facts need no verification read.
            entry.Manager.PublishMetadata(entry, entry.Metadata, GpuMetadataChange.Metadata);
            return;
        }
        if (known && command is { Eligible: true, Mode: GpuFramebufferAttachmentMode.Ordinary } accepted)
        {
            var attachments = entry.Metadata.Attachments;
            var descriptor = new GpuFramebufferImageMetadata(GpuResourceKind.Texture, accepted.Texture,
                accepted.Level, Math.Max(0, accepted.Layer), CubeFace.None, accepted.Layer < 0,
                Mode: GpuFramebufferAttachmentMode.Ordinary);
            if (accepted.Slot == FramebufferSlot.DepthStencil)
            {
                attachments = accepted.Texture == 0
                    ? attachments.Remove(FramebufferSlot.Depth).Remove(FramebufferSlot.Stencil)
                    : attachments.SetItem(FramebufferSlot.Depth, descriptor).SetItem(FramebufferSlot.Stencil, descriptor);
            }
            else attachments = accepted.Texture == 0 ? attachments.Remove(accepted.Slot) : attachments.SetItem(accepted.Slot, descriptor);
            // The live original target, realized array image and legal selection prove this
            // allocation-free effect. Canonical dependency/publication authority stays in the manager.
            entry.Manager.PublishMetadata(entry, entry.Metadata with { Attachments = attachments }, GpuMetadataChange.Metadata);
            return;
        }
        if (command.HasValue) entry.Manager.RecordFramebufferAttachmentCommand(entry, command.Value);
        bool validSlot = command is { } selected && (selected.Slot is FramebufferSlot.Depth
            or FramebufferSlot.Stencil or FramebufferSlot.DepthStencil
            || selected.Slot >= FramebufferSlot.Color0
                && (int)selected.Slot - (int)FramebufferSlot.Color0 < GpuSupport.Graphics.MaxColorAttachments);
        bool success;
        if (known && (validSlot || parameter.HasValue))
        {
            success = entry.Manager.RefreshFramebufferField(entry, validSlot ? command!.Value.Slot : null, parameter.HasValue ? OpenGLResourceValues.FromNative(parameter.Value) : null);
        }
        else success = entry.Manager.RefreshMetadata(entry, GpuMetadataChange.Metadata);
        if (!success)
            throw new InvalidOperationException("Native framebuffer metadata is unavailable after mutation.");
    }
    #endregion
}
