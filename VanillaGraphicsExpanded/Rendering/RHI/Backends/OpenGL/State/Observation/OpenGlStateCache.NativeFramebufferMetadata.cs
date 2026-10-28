using VanillaGraphicsExpanded.Rendering.Integration.OpenGL;
using System;
using OpenTK.Graphics.OpenGL;

namespace VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;

/// <summary>Observes framebuffer descriptors while preserving split native bindings and retained attachment ownership.</summary>
internal sealed partial class OpenGlStateCache
{
    #region Internal API
    /// <summary>Observes represented attachmentless defaults; other parameters have no canonical field.</summary>
    internal static NativeFramebufferMetadata BeginNativeFramebufferParameter(int selector, bool named, int parameter)
    {
        return parameter is >= (int)FramebufferDefaultParameter.FramebufferDefaultWidth and <= (int)FramebufferDefaultParameter.FramebufferDefaultFixedSampleLocations
            ? BeginNativeFramebufferMetadata(selector, named, (FramebufferDefaultParameter)parameter) : default;
    }

    /// <summary>Resolves the actual framebuffer before native descriptor mutation using existing cache recovery.</summary>
    internal static NativeFramebufferMetadata BeginNativeFramebufferMetadata(int selector, bool named, FramebufferDefaultParameter? parameter = null)
    {
        var manager = GpuResourceManagerSystem.CurrentManager;
        if (manager?.CanObserveNativeMetadata != true && Current.resourceOperation is null) return default;
        if (!named && (FramebufferTarget)selector is not (FramebufferTarget.Framebuffer or FramebufferTarget.DrawFramebuffer or FramebufferTarget.ReadFramebuffer)) return default;
        int id = named ? selector : Current.GetCurrentFramebuffer((FramebufferTarget)selector);
        Current.RequireResourceOperationAvailable(GpuResourceKind.Framebuffer, id);
        if (manager?.CanObserveNativeMetadata != true) return default;
        var entry = manager.FindObservedResource(GpuResourceKind.Framebuffer, id) as GpuResourceMetadataEntry<GpuFramebufferMetadata>;
        if (entry is null) return default;
        Current.RequireMutableFramebufferConfiguration(id);
        entry.RequireMutableStorage();
        entry.Reader ??= GpuFramebufferMetadataReader.Instance;
        if (entry.OperationInProgress || entry.CompletionInProgress) throw new InvalidOperationException("A framebuffer operation is already in progress.");
        bool known = entry.IsUsable;
        entry.OperationInProgress = true;
        entry.BeginRefresh(GpuMetadataChange.Metadata);
        return new(entry, known, parameter: parameter);
    }

    /// <summary>Retains explicit array-selection arguments and certifies mode only when admission conditions are proven.</summary>
    internal static NativeFramebufferMetadata BeginNativeFramebufferAttachment(int selector, bool named,
        int slot, int texture, int level, int layer, int family)
    {
        var observation = BeginNativeFramebufferMetadata(selector, named);
        var entry = observation.Entry;
        if (entry is null) return observation;
        try
        {
            var limits = GpuSupport.Graphics;
            bool validSlot = slot is (int)FramebufferAttachment.DepthAttachment or (int)FramebufferAttachment.StencilAttachment
                or (int)FramebufferAttachment.DepthStencilAttachment
                || slot >= (int)FramebufferAttachment.ColorAttachment0
                    && (long)slot - (int)FramebufferAttachment.ColorAttachment0 < limits.MaxColorAttachments;
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

            // Invalid native slots still reach OpenGL unchanged. They have no canonical selection.
            if (!validSlot) return new(entry, observation.Known, provenRejected: true);
            bool eligible = family is 0 or 1 && !rejected && imageExists;
            return new(entry, observation.Known, new(OpenGLResourceValues.FromNative((FramebufferAttachment)slot), texture, level, family == 1 ? layer : -1,
                0, 0, GpuFramebufferAttachmentMode.Ordinary, eligible, rejected));
        }
        catch
        {
            // No token reaches the dispatch owner when argument inspection fails.
            observation.Complete(false);
            throw;
        }
    }
    #endregion

    /// <summary>Retains the original framebuffer registration until native descriptor acquisition finishes.</summary>
    internal readonly struct NativeFramebufferMetadata(GpuResourceMetadataEntry<GpuFramebufferMetadata>? entry,
        bool known, FramebufferAttachmentCommand? command = null, FramebufferDefaultParameter? parameter = null, bool provenRejected = false)
    {
        #region Internal API
        /// <summary>Exposes the original entry while constructing an attachment-specific observation.</summary>
        internal GpuResourceMetadataEntry<GpuFramebufferMetadata>? Entry => entry;
        /// <summary>Identifies whether unaffected facts were available before mutation.</summary>
        internal bool Known => known;
        /// <summary>Publishes real descriptors and invalidates every associated helper, including on acquisition failure.</summary>
        internal void Complete(bool nativeReturned)
        {
            if (entry is null) return;
            entry.OperationInProgress = false;
            if (!entry.Manager.IsRegistered(entry) || entry.State != GpuResourceRegistrationState.Live) return;
            try
            {
                if (!nativeReturned) return;
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
            catch when (!nativeReturned) { /* Preserve the native dispatch exception. */ }
            finally
            {
                // A callback can change the current context during native dispatch.
                // Availability stays withdrawn, but old-context helpers must not run there.
                try { if (entry.Manager.CanObserveNativeMetadata) entry.NotifyFramebufferDependents(); }
                catch when (!nativeReturned) { /* Preserve the original native dispatch exception. */ }
            }
        }
        #endregion
    }
}
