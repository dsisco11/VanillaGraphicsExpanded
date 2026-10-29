using VanillaGraphicsExpanded.Rendering.RHI;

namespace VanillaGraphicsExpanded.Rendering;

/// <summary>Combines native attachment facts with proven command semantics where GL has no mode query.</summary>
internal sealed partial class GpuResourceManager
{
    #region Private
    /// <summary>Preserves compatible prior evidence and certifies only matching, admitted attachment commands.</summary>
    private T ReconcileFramebufferAttachmentModes<T>(GpuResourceMetadataEntry<T> entry, T candidate) where T : struct
    {
        if (candidate is not GpuFramebufferMetadata actual || actual.Attachments is null) return candidate;
        var previous = entry.Metadata is GpuFramebufferMetadata old ? old.Attachments : null;
        var attachments = actual.Attachments.ToBuilder();
        foreach (var pair in actual.Attachments)
        {
            var image = pair.Value;
            if (image.OriginalBacking is null)
            {
                // A descriptor read cannot distinguish an old retained object from
                // a replacement that happens to reuse the same numeric name.
                var original = previous is not null && previous.TryGetValue(pair.Key, out var priorIdentity)
                    && priorIdentity.Kind == image.Kind && priorIdentity.Id == image.Id
                    ? priorIdentity.OriginalBacking : FindObservedResource(image.Kind, image.Id);
                image = image with { OriginalBacking = original };
            }
            var mode = image.Mode != GpuFramebufferAttachmentMode.Unknown ? image.Mode : (!GpuSupport.HasCachedCapabilitiesForCurrentContext || GpuSupport.Graphics.SupportsOvrMultiview)
                ? GpuFramebufferAttachmentMode.Unknown : GpuFramebufferAttachmentMode.Ordinary;
            if (image.Kind != GpuResourceKind.Texture || FindResource(image.Kind, image.Id)
                is GpuResourceMetadataEntry<GpuTextureMetadata> backing
                    && backing.Metadata.Target != 0 && backing.Metadata.Target != TextureDimension.TwoArray)
                mode = GpuFramebufferAttachmentMode.Ordinary;
            bool unchanged = previous is not null && previous.TryGetValue(pair.Key, out var prior)
                && (prior with { Mode = GpuFramebufferAttachmentMode.Unknown }) == (image with { Mode = GpuFramebufferAttachmentMode.Unknown });
            if (unchanged && image.Mode == GpuFramebufferAttachmentMode.Unknown) mode = previous![pair.Key].Mode;
            attachments[pair.Key] = image with { Mode = mode };
        }
        return (T)(object)(actual with { Attachments = attachments.ToImmutable() });
    }
    #endregion
}
