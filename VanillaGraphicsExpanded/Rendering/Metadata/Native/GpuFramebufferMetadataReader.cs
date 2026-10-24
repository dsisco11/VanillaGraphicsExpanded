using System.Collections.Immutable;
using OpenTK.Graphics.OpenGL;

namespace VanillaGraphicsExpanded.Rendering;

/// <summary>Captures actual attachment descriptors and attachmentless defaults without consulting wrapper ownership policy.</summary>
internal sealed class GpuFramebufferMetadataReader : IGpuResourceMetadataReader<GpuFramebufferMetadata>
{
    internal static readonly GpuFramebufferMetadataReader Instance = new();

    #region Public API
    /// <inheritdoc />
    public bool IsResource(nint id) => id != 0 && GL.IsFramebuffer(unchecked((int)id));

    /// <inheritdoc />
    public bool TryRead(nint id, out GpuFramebufferMetadata metadata)
    {
        metadata = default;
        if (!IsResource(id)) return false;
        using var binding = StateCache.Current.BindFramebufferScope(FramebufferTarget.ReadFramebuffer, unchecked((int)id));
        var attachments = ImmutableDictionary.CreateBuilder<FramebufferAttachment, GpuFramebufferImageMetadata>();
        int limit = GpuSupport.Graphics.MaxColorAttachments;
        for (int index = 0; index < limit; index++)
            if (!ReadAttachment(FramebufferAttachment.ColorAttachment0 + index, attachments)) return false;
        if (!ReadAttachment(FramebufferAttachment.DepthAttachment, attachments)
            || !ReadAttachment(FramebufferAttachment.StencilAttachment, attachments)) return false;
        int width = ReadDefault(id, (FramebufferDefaultParameter)0x9310);
        int height = ReadDefault(id, (FramebufferDefaultParameter)0x9311);
        int layers = ReadDefault(id, (FramebufferDefaultParameter)0x9312);
        int samples = ReadDefault(id, (FramebufferDefaultParameter)0x9313);
        int fixedSamples = ReadDefault(id, (FramebufferDefaultParameter)0x9314);
        if (width < 0 || height < 0 || layers < 0 || samples < 0 || fixedSamples < 0) return false;
        metadata = new(attachments.ToImmutable(), width, height, layers, samples, fixedSamples != 0);
        return true;
    }
    #endregion

    #region Private
    /// <summary>Reads one selected image without resolving a deleted retained name as a new allocation.</summary>
    private static bool ReadAttachment(FramebufferAttachment slot,
        ImmutableDictionary<FramebufferAttachment, GpuFramebufferImageMetadata>.Builder attachments)
    {
        int type = Read(slot, FramebufferParameterName.FramebufferAttachmentObjectType);
        if (type == (int)All.None) return true;
        int name = Read(slot, FramebufferParameterName.FramebufferAttachmentObjectName);
        if (name <= 0) return false;
        if (type == (int)All.Renderbuffer)
        {
            attachments.Add(slot, new(GpuResourceKind.Renderbuffer, name, 0, 0, 0, false));
            return true;
        }
        if (type != (int)All.Texture) return false;
        int level = Read(slot, FramebufferParameterName.FramebufferAttachmentTextureLevel);
        int layer = Read(slot, FramebufferParameterName.FramebufferAttachmentTextureLayer);
        int face = Read(slot, FramebufferParameterName.FramebufferAttachmentTextureCubeMapFace);
        int layered = Read(slot, FramebufferParameterName.FramebufferAttachmentLayered);
        if (level < 0 || layer < 0 || face < 0 || layered < 0) return false;
        attachments.Add(slot, new(GpuResourceKind.Texture, name, level, layer, (TextureTarget)face, layered != 0));
        return true;
    }

    /// <summary>Uses an invalid sentinel to detect unavailable attachment facts without consuming errors.</summary>
    private static int Read(FramebufferAttachment slot, FramebufferParameterName parameter)
    {
        int value = -1;
        GL.GetFramebufferAttachmentParameter(FramebufferTarget.ReadFramebuffer, slot, parameter, out value);
        return value;
    }

    /// <summary>Reads one attachmentless default using the native object name.</summary>
    private static int ReadDefault(nint id, FramebufferDefaultParameter parameter)
    {
        int value = -1;
        if (GpuSupport.Graphics.SupportsArbDirectStateAccess)
            GL.GetNamedFramebufferParameter(unchecked((int)id), parameter, out value);
        else
            GL.GetFramebufferParameter(FramebufferTarget.ReadFramebuffer, parameter, out value);
        return value;
    }
    #endregion
}
