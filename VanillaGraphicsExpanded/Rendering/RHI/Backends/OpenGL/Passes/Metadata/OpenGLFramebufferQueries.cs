using System.Collections.Immutable;
using OpenTK.Graphics.OpenGL;
using Native = OpenTK.Graphics.OpenGL4;

namespace VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;

/// <summary>Captures actual attachment descriptors and attachmentless defaults without consulting wrapper ownership policy.</summary>
internal readonly struct OpenGLFramebufferQueries(OpenGLBackend backend)
{
    #region Public API
    /// <summary>Acquires unknown framebuffer facts without manager publication.</summary>
    public bool IsResource(nint id)
    {
        backend.RequireContext();
        bool result = id != 0 && Native.GL.IsFramebuffer(unchecked((int)id));
        backend.RequireContext();
        return result;
    }

    /// <summary>Acquires unknown framebuffer facts without manager publication.</summary>
    public bool TryRead(nint id, out GpuFramebufferMetadata metadata)
    {
        metadata = default;
        if (!IsResource(id)) return false;
        var attachments = ImmutableDictionary.CreateBuilder<FramebufferSlot, GpuFramebufferImageMetadata>();
        int limit = backend.NativeCapabilities.MaxColorAttachments;
        for (int index = 0; index < limit; index++)
            if (!ReadAttachment(id, FramebufferSlot.Color0 + index, attachments)) return false;
        if (!ReadAttachment(id, FramebufferSlot.Depth, attachments)
            || !ReadAttachment(id, FramebufferSlot.Stencil, attachments)) return false;
        int width = ReadDefault(id, FramebufferMetadataField.Width);
        int height = ReadDefault(id, FramebufferMetadataField.Height);
        int layers = ReadDefault(id, FramebufferMetadataField.Layers);
        int samples = ReadDefault(id, FramebufferMetadataField.Samples);
        int fixedSamples = ReadDefault(id, FramebufferMetadataField.FixedSampleLocations);
        if (width < 0 || height < 0 || layers < 0 || samples < 0 || fixedSamples < 0) return false;
        metadata = new(attachments.ToImmutable(), width, height, layers, samples, fixedSamples != 0);
        return true;
    }
    #endregion

    #region Internal API
    /// <summary>Recovers only one affected slot or one attachmentless field of an otherwise known descriptor.</summary>
    internal bool TryReadAffected(nint id, GpuFramebufferMetadata previous, FramebufferSlot? slot,
        FramebufferMetadataField? parameter, out GpuFramebufferMetadata candidate)
    {
        backend.RequireContext();
        candidate = previous;
        if (slot.HasValue)
        {
            var attachments = previous.Attachments.ToBuilder();
            if (slot == FramebufferSlot.DepthStencil)
            {
                attachments.Remove(FramebufferSlot.Depth);
                attachments.Remove(FramebufferSlot.Stencil);
                if (!ReadAttachment(id, FramebufferSlot.Depth, attachments)
                    || !ReadAttachment(id, FramebufferSlot.Stencil, attachments)) return false;
            }
            else
            {
                attachments.Remove(slot.Value);
                if (!ReadAttachment(id, slot.Value, attachments)) return false;
            }
            candidate = previous with { Attachments = attachments.ToImmutable() };
        }
        else if (parameter.HasValue)
        {
            int value = ReadDefault(id, parameter.Value);
            if (value < 0) return false;
            candidate = parameter.Value switch
            {
                FramebufferMetadataField.Width => previous with { DefaultWidth = value },
                FramebufferMetadataField.Height => previous with { DefaultHeight = value },
                FramebufferMetadataField.Layers => previous with { DefaultLayers = value },
                FramebufferMetadataField.Samples => previous with { DefaultSamples = value },
                FramebufferMetadataField.FixedSampleLocations => previous with { DefaultFixedSampleLocations = value != 0 },
                _ => previous
            };
        }
        return true;
    }
    #endregion

    #region Private
    /// <summary>Reads one selected image without resolving a deleted retained name as a new allocation.</summary>
    private bool ReadAttachment(nint id, FramebufferSlot slot,
        ImmutableDictionary<FramebufferSlot, GpuFramebufferImageMetadata>.Builder attachments)
    {
        int type = Read(id, slot, FramebufferParameterName.FramebufferAttachmentObjectType);
        if (type == (int)All.None) return true;
        int name = Read(id, slot, FramebufferParameterName.FramebufferAttachmentObjectName);
        if (name <= 0) return false;
        if (type == (int)All.Renderbuffer)
        {
            attachments.Add(slot, new(GpuResourceKind.Renderbuffer, name, 0, 0, 0, false));
            return true;
        }
        if (type != (int)All.Texture) return false;
        int level = Read(id, slot, FramebufferParameterName.FramebufferAttachmentTextureLevel);
        int layer = Read(id, slot, FramebufferParameterName.FramebufferAttachmentTextureLayer);
        int face = Read(id, slot, FramebufferParameterName.FramebufferAttachmentTextureCubeMapFace);
        int layered = Read(id, slot, FramebufferParameterName.FramebufferAttachmentLayered);
        bool multiview = backend.NativeCapabilities.SupportsOvrMultiview;
        int baseView = multiview ? Read(id, slot, (FramebufferParameterName)All.FramebufferAttachmentTextureBaseViewIndexOvr) : 0;
        int views = multiview ? Read(id, slot, (FramebufferParameterName)All.FramebufferAttachmentTextureNumViewsOvr) : 0;
        if (level < 0 || layer < 0 || face < 0 || layered < 0 || baseView < 0 || views < 0) return false;
        attachments.Add(slot, new(GpuResourceKind.Texture, name, level, layer, OpenGLImageValues.Face((TextureTarget)face), layered != 0, baseView, views));
        return true;
    }

    /// <summary>Uses an invalid sentinel to detect unavailable attachment facts without consuming errors.</summary>
    private int Read(nint id, FramebufferSlot slot, FramebufferParameterName parameter)
    {
        int value = -1;
        backend.RequireContext();
        Native.GL.GetNamedFramebufferAttachmentParameter(unchecked((int)id), (Native.FramebufferAttachment)OpenGLResourceValues.Native(slot), (Native.FramebufferParameterName)parameter, out value);
        backend.RequireContext();
        return value;
    }

    /// <summary>Reads one attachmentless default using the native object name.</summary>
    private int ReadDefault(nint id, FramebufferMetadataField parameter)
    {
        int value = -1;
        backend.RequireContext();
        Native.GL.GetNamedFramebufferParameter(unchecked((int)id), (Native.FramebufferDefaultParameter)OpenGLResourceValues.Native(parameter), out value);
        backend.RequireContext();
        return value;
    }
    #endregion
}
