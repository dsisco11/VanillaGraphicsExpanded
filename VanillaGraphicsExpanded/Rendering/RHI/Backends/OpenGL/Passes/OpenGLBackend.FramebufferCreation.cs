using System;
using System.Collections.Immutable;
using OpenTK.Graphics.OpenGL4;

namespace VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;

/// <summary>Creates framebuffer configurations through core named commands and the existing private manager handoff.</summary>
internal sealed partial class OpenGLBackend
{
    #region Internal API
    /// <summary>Allocates and configures one candidate without selecting a framebuffer or reading submitted attachment facts.</summary>
    public GpuFramebufferMetadata? CreateFramebuffer(in GpuFramebufferInitialization request, out nint id)
    {
        RequireContext();
        GL.CreateFramebuffers(1, out int name);
        id = name;
        RequireContext();
        if (name == 0) return null;
        cache.InvalidateFramebufferDrawRouting(name);
        if (request.HasDefaults)
        {
            GL.NamedFramebufferParameter(name, FramebufferDefaultParameter.FramebufferDefaultWidth, request.DefaultWidth);
            GL.NamedFramebufferParameter(name, FramebufferDefaultParameter.FramebufferDefaultHeight, request.DefaultHeight);
            GL.NamedFramebufferParameter(name, FramebufferDefaultParameter.FramebufferDefaultLayers, request.DefaultLayers);
            GL.NamedFramebufferParameter(name, FramebufferDefaultParameter.FramebufferDefaultSamples, request.DefaultSamples);
            GL.NamedFramebufferParameter(name, FramebufferDefaultParameter.FramebufferDefaultFixedSampleLocations, request.DefaultFixedSampleLocations ? 1 : 0);
            RequireContext();
        }
        var candidate = new GpuFramebufferMetadata(
            ImmutableDictionary<OpenTK.Graphics.OpenGL.FramebufferAttachment, GpuFramebufferImageMetadata>.Empty,
            request.DefaultWidth, request.DefaultHeight, request.DefaultLayers, request.DefaultSamples, request.DefaultFixedSampleLocations);
        foreach (var pair in request.Attachments)
        {
            AttachFramebufferImage(name, (FramebufferAttachment)pair.Key, pair.Value);
            RequireContext();
            candidate = StateCache.WithFramebufferAttachment(candidate, pair.Key, pair.Value);
        }
        return candidate;
    }
    #endregion

    #region Private
    /// <summary>Translates a proven image selection to a single named attachment command, preserving face and layered semantics.</summary>
    private void AttachFramebufferImage(int name, FramebufferAttachment slot, GpuFramebufferAttachment image)
    {
        if (image.AttachmentMode == GpuFramebufferAttachmentMode.Multiview)
            throw new NotSupportedException("Multiview framebuffer creation requires an explicitly supported backend operation.");
        if (image.Kind == GpuResourceKind.Renderbuffer)
            GL.NamedFramebufferRenderbuffer(name, slot, RenderbufferTarget.Renderbuffer, image.RenderbufferId);
        else if (image.Layer is int layer)
            GL.NamedFramebufferTextureLayer(name, slot, image.TextureId, image.MipLevel, layer);
        else if (image.CubeFace is { } face)
            GL.NamedFramebufferTextureLayer(name, slot, image.TextureId, image.MipLevel,
                (int)face - (int)OpenTK.Graphics.OpenGL.TextureTarget.TextureCubeMapPositiveX);
        else GL.NamedFramebufferTexture(name, slot, image.TextureId, image.MipLevel);
    }
    #endregion
}
