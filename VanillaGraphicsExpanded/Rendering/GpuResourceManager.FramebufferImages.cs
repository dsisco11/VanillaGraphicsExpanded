using System.Collections.Generic;
using OpenTK.Graphics.OpenGL;
namespace VanillaGraphicsExpanded.Rendering;
/// <summary>Resolves descriptor backings once and projects exact image selections through canonical manager facts.</summary>
internal sealed partial class GpuResourceManager
{
    #region Internal API
    /// <summary>Builds a complete image candidate before the framebuffer commits its retained composition.</summary>
    internal static Dictionary<FramebufferAttachment, GpuFramebufferAttachment> ResolveFramebufferAttachments(
        GpuFramebuffer owner, IReadOnlyDictionary<FramebufferAttachment, GpuFramebufferAttachment> images, bool recover, GpuFramebufferMetadata? candidateMetadata = null)
    {
        var descriptors = candidateMetadata?.Attachments ?? owner.PublishedMetadata.Attachments
            ?? throw new System.InvalidOperationException("Framebuffer descriptors are unavailable.");
        var candidate = new System.Collections.Generic.Dictionary<FramebufferAttachment, GpuFramebufferAttachment>(images);
        foreach (var slot in new System.Collections.Generic.List<FramebufferAttachment>(candidate.Keys))
            if (!descriptors.ContainsKey(slot)) candidate.Remove(slot);
        // Packed depth/stencil and repeated color selections describe the same image.
        // Preserve that relationship so combined-aspect helpers retain one reference.
        var resolved = new System.Collections.Generic.Dictionary<GpuFramebufferImageMetadata, GpuFramebufferAttachment>();
        var textures = new System.Collections.Generic.Dictionary<nint, GpuTextureMetadata>();
        var renderbuffers = new System.Collections.Generic.Dictionary<nint, GpuRenderbufferMetadata>();
        foreach (var pair in descriptors)
        {
            var data = pair.Value;
            if (resolved.TryGetValue(data, out var shared))
            {
                candidate[pair.Key] = shared;
                continue;
            }
            if (!recover && candidate.TryGetValue(pair.Key, out var current) && current.ResourceId == data.Id
                && current.Kind == data.Kind && current.MatchesNativeSelection(data,
                    retainAuthoredMode: owner.ResourceManager is null || owner.ResourceManager.IsMetadataRegistryRetired
                        || owner.ResourceManager.GetResourceRegistration(owner) is null))
            {
                resolved.Add(data, current);
                continue;
            }
            var backing = owner.ResourceManager is { IsMetadataRegistryRetired: false } activeManager
                ? activeManager.FindResource(data.Kind, data.Id) : null;
            var target = data.CubeFace != 0 ? data.CubeFace
                : backing is GpuResourceMetadataEntry<GpuTextureMetadata> texture ? texture.Metadata.Target : 0;
            GpuTextureMetadata? acquiredTexture = null;
            GpuRenderbufferMetadata? acquiredRenderbuffer = null;
            if (data.Kind == GpuResourceKind.Texture)
            {
                if (!textures.TryGetValue(data.Id, out var textureFacts))
                {
                    // Reuse one complete backing snapshot across distinct layers/faces of the same image.
                    var storageTarget = data.CubeFace != 0 ? TextureTarget.TextureCubeMap
                        : backing is GpuResourceMetadataEntry<GpuTextureMetadata> known ? known.Metadata.Target : 0;
                    textureFacts = AcquireFramebufferTexture(checked((int)data.Id), storageTarget, recover);
                    textures.Add(data.Id, textureFacts);
                }
                acquiredTexture = textureFacts;
                target = data.CubeFace != 0 ? data.CubeFace : textureFacts.Target;
            }
            else
            {
                if (!renderbuffers.TryGetValue(data.Id, out var renderbufferFacts))
                {
                    renderbufferFacts = AcquireFramebufferRenderbuffer(checked((int)data.Id), recover);
                    renderbuffers.Add(data.Id, renderbufferFacts);
                }
                acquiredRenderbuffer = renderbufferFacts;
            }
            bool range = target == TextureTarget.Texture2DArray && data.Mode != GpuFramebufferAttachmentMode.Ordinary
                && data.ViewCount > 0 && (data.Layered || data.Mode == GpuFramebufferAttachmentMode.Multiview);
            var image = GpuFramebufferAttachment.FromExternalImage(checked((int)data.Id), data.Kind == GpuResourceKind.Renderbuffer,
                target, data.MipLevel,
                !data.Layered && data.Mode != GpuFramebufferAttachmentMode.Multiview
                    && target is TextureTarget.Texture1DArray or TextureTarget.Texture2DArray
                    or TextureTarget.Texture3D or TextureTarget.TextureCubeMapArray or TextureTarget.Texture2DMultisampleArray ? data.Layer : null,
                acquiredTexture, range ? data.BaseView : 0, range ? data.ViewCount : 0, data.Mode, acquiredRenderbuffer);
            candidate[pair.Key] = image;
            resolved.Add(data, image);
        }
        return candidate;
    }
    #endregion
}
