using System;
using OpenTK.Graphics.OpenGL;

namespace VanillaGraphicsExpanded.Rendering;

/// <summary>Observes framebuffer descriptors while preserving split native bindings and retained attachment ownership.</summary>
internal sealed partial class StateCache
{
    #region Internal API
    /// <summary>Observes represented attachmentless defaults; other parameters have no canonical field.</summary>
    internal static NativeFramebufferMetadata BeginNativeFramebufferParameter(int selector, bool named, int parameter)
    {
        return parameter is >= (int)FramebufferDefaultParameter.FramebufferDefaultWidth and <= (int)FramebufferDefaultParameter.FramebufferDefaultFixedSampleLocations
            ? BeginNativeFramebufferMetadata(selector, named) : default;
    }

    /// <summary>Resolves the actual framebuffer before native descriptor mutation using existing cache recovery.</summary>
    internal static NativeFramebufferMetadata BeginNativeFramebufferMetadata(int selector, bool named)
    {
        var manager = GpuResourceManagerSystem.CurrentManager;
        if (manager?.CanObserveNativeMetadata != true) return default;
        if (!named && (FramebufferTarget)selector is not (FramebufferTarget.Framebuffer or FramebufferTarget.DrawFramebuffer or FramebufferTarget.ReadFramebuffer)) return default;
        int id = named ? selector : Current.GetCurrentFramebuffer((FramebufferTarget)selector);
        var entry = manager.FindObservedResource(GpuResourceKind.Framebuffer, id) as GpuResourceMetadataEntry<GpuFramebufferMetadata>;
        if (entry is null) return default;
        Current.RequireMutableFramebufferConfiguration(id);
        entry.RequireMutableStorage();
        entry.Reader ??= GpuFramebufferMetadataReader.Instance;
        entry.BeginRefresh(GpuMetadataChange.Metadata);
        return new(entry);
    }

    /// <summary>Retains explicit array-selection arguments and certifies mode only when admission conditions are proven.</summary>
    internal static NativeFramebufferMetadata BeginNativeFramebufferAttachment(int selector, bool named,
        int slot, int texture, int level, int layer, int family)
    {
        var observation = BeginNativeFramebufferMetadata(selector, named);
        var entry = observation.Entry;
        if (entry is null) return observation;
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
        if (source?.IsUsable == true && source.Metadata.Target == TextureTarget.Texture2DArray)
            foreach (var image in source.Metadata.Levels)
                if (image.Level == level && image.Allocated) { imageExists = true; break; }

        bool eligible = family is 0 or 1 && !rejected && imageExists;
        return new(entry, new((FramebufferAttachment)slot, texture, level, family == 1 ? layer : -1,
            0, 0, GpuFramebufferAttachmentMode.Ordinary, eligible, rejected));
    }
    #endregion

    /// <summary>Retains the original framebuffer registration until native descriptor acquisition finishes.</summary>
    internal readonly struct NativeFramebufferMetadata(GpuResourceMetadataEntry<GpuFramebufferMetadata>? entry,
        FramebufferAttachmentCommand? command = null)
    {
        #region Internal API
        /// <summary>Exposes the original entry while constructing an attachment-specific observation.</summary>
        internal GpuResourceMetadataEntry<GpuFramebufferMetadata>? Entry => entry;
        /// <summary>Publishes real descriptors and invalidates every associated helper, including on acquisition failure.</summary>
        internal void Complete(bool nativeReturned)
        {
            if (entry is null || !entry.Manager.IsRegistered(entry) || entry.State != GpuResourceRegistrationState.Live) return;
            try
            {
                if (!nativeReturned) return;
                if (!entry.Manager.CanObserveNativeMetadata)
                    throw new InvalidOperationException("Framebuffer metadata completion requires its originating context.");
                if (command.HasValue) entry.Manager.RecordFramebufferAttachmentCommand(entry, command.Value);
                if (!entry.Manager.RefreshMetadata(entry, GpuMetadataChange.Metadata))
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
