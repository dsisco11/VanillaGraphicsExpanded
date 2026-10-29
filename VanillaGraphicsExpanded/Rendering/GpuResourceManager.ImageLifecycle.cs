using System.Collections.Immutable;
using VanillaGraphicsExpanded.Rendering.RHI;

namespace VanillaGraphicsExpanded.Rendering;

/// <summary>Maintains image-object reservations and their kind-specific realization defaults.</summary>
internal sealed partial class GpuResourceManager
{
    #region Internal API
    /// <summary>Records integration proof of an image object predating hook installation, permitting one discovery.</summary>
    internal GpuResourceMetadataEntry<T> RecordPreexistingImage<T>(GpuResourceKind kind, nint id) where T : struct
    {
        RequireMetadataContext();
        if (resourceObservationActive || kind is not (GpuResourceKind.Texture or GpuResourceKind.Renderbuffer or GpuResourceKind.Framebuffer))
            throw new System.InvalidOperationException("Image discovery provenance must precede observation activation.");
        if (FindResource(kind, id) is not null)
            throw new System.InvalidOperationException("The original image identity is already registered.");
        var entry = RegisterCreatedResource<T>(kind, id);
        entry.Realize();
        entry.InitialDiscovery = GpuInitialDiscovery.Preexisting;
        return entry;
    }

    /// <summary>Consumes returned names without replacing an existing original allocation.</summary>
    internal void ObserveImageCreation(GpuResourceKind kind, nint id, bool realized, TextureDimension target = TextureDimension.Undefined)
    {
        if (id == 0 || !CanObserveNativeMetadata || FindResource(kind, id) is not null) return;
        switch (kind)
        {
            case GpuResourceKind.Texture:
                RegisterCreatedResource<GpuTextureMetadata>(kind, id);
                break;
            case GpuResourceKind.Renderbuffer:
                RegisterCreatedResource<GpuRenderbufferMetadata>(kind, id);
                break;
            case GpuResourceKind.Framebuffer:
                RegisterCreatedResource<GpuFramebufferMetadata>(kind, id);
                break;
            default: return;
        }
        if (realized) RealizeObservedImage(kind, id, target, false);
    }

    /// <summary>Realizes known reservations and records unknown EXT identities without inventing their defaults.</summary>
    internal GpuResourceRegistration? RealizeObservedImage(GpuResourceKind kind, nint id,
        TextureDimension target = TextureDimension.Undefined, bool implicitCreation = false)
    {
        if (id == 0 || !CanObserveNativeMetadata) return null;
        if (FindResource(kind, id) is null && implicitCreation)
        {
            // The EXT operation may have initialized an unused name or accessed an older
            // object. Retain identity, but only explicit discovery may establish its facts.
            ObserveImageCreation(kind, id, false);
            var unknown = FindObservedResource(kind, id);
            if (unknown is null) return null;
            unknown.Realize();
            unknown.InitialDiscovery = GpuInitialDiscovery.ImplicitCreation;
            return unknown;
        }
        var entry = FindObservedResource(kind, id);
        if (entry is null || entry.IsRealized) return entry;
        // Realization proves object existence, but cannot erase an unresolved earlier effect.
        if (entry is GpuResourceMetadataEntry<GpuTextureMetadata> texture && target != TextureDimension.Undefined)
        {
            if (texture.PendingChange != GpuMetadataChange.None) { texture.Realize(); return texture; }
            PublishMetadata(texture, new(target, ImmutableArray<GpuTextureLevelMetadata>.Empty, false, 0, 0,
                target is TextureDimension.Buffer or TextureDimension.TwoMultisample or TextureDimension.TwoMultisampleArray ? 0 : 1000), GpuMetadataChange.None);
        }
        else if (entry is GpuResourceMetadataEntry<GpuRenderbufferMetadata> renderbuffer)
        {
            if (renderbuffer.PendingChange != GpuMetadataChange.None) { renderbuffer.Realize(); return renderbuffer; }
            PublishMetadata(renderbuffer, new(0, 0, TextureFormat.Rgba, 0), GpuMetadataChange.None);
        }
        else if (entry is GpuResourceMetadataEntry<GpuFramebufferMetadata> framebuffer)
        {
            if (framebuffer.PendingChange != GpuMetadataChange.None) { framebuffer.Realize(); return framebuffer; }
            PublishMetadata(framebuffer, new(ImmutableDictionary<FramebufferSlot, GpuFramebufferImageMetadata>.Empty,
                0, 0, 0, 0, true), GpuMetadataChange.None);
        }
        return entry;
    }
    #endregion
}
