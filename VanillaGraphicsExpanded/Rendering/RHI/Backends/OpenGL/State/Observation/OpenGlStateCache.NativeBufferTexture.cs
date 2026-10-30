using System;
using OpenTK.Graphics.OpenGL;

namespace VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;

/// <summary>Maintains native buffer texture associations from supplied ranges and original backing facts.</summary>
internal sealed partial class OpenGlStateCache
{
    #region Internal API
    /// <summary>Applies an association once without probing its descriptor or narrowing native-width ranges.</summary>
    internal static void ObserveBufferTexture(int selector, int target, int unit,
        bool named, bool explicitUnit, int buffer, int format, nint offset, nint size, bool whole, bool extNamed = false)
    {
        if (target != (int)TextureTarget.TextureBuffer || !OpenGLObservationContext.TryGet(out var cache, out _, false)) return;
        var entry = ResolveObservedTexture(selector, target, unit, named, explicitUnit, extNamed);
        // Unknown EXT objects have no target certification yet. Retain validated input
        // facts on their existing entry; explicit discovery will verify the actual target.
        if (entry is null && extNamed)
            entry = RHIResourceManagerSystem.CurrentManager?.FindObservedResource(GpuResourceKind.Texture, selector)
                is GpuResourceMetadataEntry<GpuTextureMetadata> { IsUsable: false, HasCommittedMetadata: false,
                    InitialDiscovery: GpuInitialDiscovery.ImplicitCreation } unknown ? unknown : null;
        if (entry?.IsRealized != true) return;
        // Deleting a known backing withdraws this association without losing the
        // other complete texture facts. A new accepted association replaces it.
        bool initial = !entry.IsUsable && entry.InitialDiscovery == GpuInitialDiscovery.ImplicitCreation;
        TextureFormat requested;
        try { requested = OpenGLImageValues.Format((PixelInternalFormat)format); }
        catch (ArgumentOutOfRangeException) { return; }
        // Validate the legal format independently of the optional nonzero backing input.
        var request = new GpuBufferTextureInitialization { Buffer = buffer == 0 ? 1 : buffer, Format = requested,
            Mode = whole || buffer == 0 ? GpuBufferTextureMode.Whole : GpuBufferTextureMode.Range };
        if (!whole && buffer != 0) { request.Offset = offset; request.Size = size; }
        try { request.Validate(); }
        catch (ArgumentException) { return; }
        var backing = entry.Manager.FindObservedResource(GpuResourceKind.Buffer, buffer) as GpuResourceMetadataEntry<GpuBufferMetadata>;
        if (buffer != 0 && backing?.IsRealized != true) { WithdrawImageDefinition(cache, entry, GpuMetadataChange.Metadata); return; }
        if (buffer != 0 && backing?.IsUsable != true) { WithdrawImageDefinition(cache, entry, GpuMetadataChange.Metadata); return; }
        if (!whole && buffer != 0)
        {
            if (offset > backing!.Metadata.Capacity || size > backing.Metadata.Capacity - offset) return;
            if (!GpuSupport.HasCachedCapabilitiesForCurrentContext) { WithdrawImageDefinition(cache, entry); return; }
            int alignment = GpuSupport.Graphics.TextureBufferOffsetAlignment;
            if (alignment <= 0 || offset % alignment != 0) return;
        }
        var association = new GpuTextureBufferMetadata(buffer, requested, whole || buffer == 0 ? 0 : offset,
            whole || buffer == 0 ? 0 : size, whole);
        if (initial)
        {
            entry.Manager.RetainInitialBufferTextureRequest(entry, association, backing);
            return;
        }
        cache.InterruptNativeResourceOperation(GpuResourceKind.Texture, (int)entry.Id);
        if (InterruptNativeMetadataCompletion(entry, GpuMetadataChange.Storage)) return;
        if (!entry.IsUsable && (!entry.HasCommittedMetadata || entry.PendingChange != GpuMetadataChange.Metadata)) return;
        entry.Manager.PublishMetadata(entry, entry.Metadata with { Buffer = association }, GpuMetadataChange.Storage, true);
    }
    #endregion
}
