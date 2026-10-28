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
        // A newly realized object starts with output zero selecting its first color attachment.
        cache.RememberFramebufferDrawRouting(name, [OpenTK.Graphics.OpenGL.DrawBuffersEnum.ColorAttachment0]);
        if (request.HasDefaults)
        {
            OpenGLDriverCalls.NamedFramebufferParameter(name, FramebufferDefaultParameter.FramebufferDefaultWidth, request.DefaultWidth);
            OpenGLDriverCalls.NamedFramebufferParameter(name, FramebufferDefaultParameter.FramebufferDefaultHeight, request.DefaultHeight);
            OpenGLDriverCalls.NamedFramebufferParameter(name, FramebufferDefaultParameter.FramebufferDefaultLayers, request.DefaultLayers);
            OpenGLDriverCalls.NamedFramebufferParameter(name, FramebufferDefaultParameter.FramebufferDefaultSamples, request.DefaultSamples);
            OpenGLDriverCalls.NamedFramebufferParameter(name, FramebufferDefaultParameter.FramebufferDefaultFixedSampleLocations, request.DefaultFixedSampleLocations ? 1 : 0);
            RequireContext();
        }
        var candidate = new GpuFramebufferMetadata(
            ImmutableDictionary<FramebufferSlot, GpuFramebufferImageMetadata>.Empty,
            request.DefaultWidth, request.DefaultHeight, request.DefaultLayers, request.DefaultSamples, request.DefaultFixedSampleLocations);
        foreach (var pair in request.Attachments)
        {
            AttachFramebufferImage(name, (FramebufferAttachment)OpenGLResourceValues.Native(pair.Key), pair.Value);
            RequireContext();
            candidate = OpenGlStateCache.WithFramebufferAttachment(candidate, pair.Key, pair.Value);
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
            OpenGLDriverCalls.NamedFramebufferRenderbuffer(name, slot, RenderbufferTarget.Renderbuffer, image.RenderbufferId);
        else if (image.Layer is int layer)
            OpenGLDriverCalls.NamedFramebufferTextureLayer(name, slot, image.TextureId, image.MipLevel, layer);
        else if (image.CubeFace is { } face)
            OpenGLDriverCalls.NamedFramebufferTextureLayer(name, slot, image.TextureId, image.MipLevel,
                (int)face - (int)CubeFace.PositiveX);
        else OpenGLDriverCalls.NamedFramebufferTexture(name, slot, image.TextureId, image.MipLevel);
    }
    #endregion
}
