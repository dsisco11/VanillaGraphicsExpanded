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
        bool named = GpuSupport.Graphics.SupportsArbDirectStateAccess;
        using var binding = named ? (StateCache.FramebufferScope?)null
            : StateCache.Current.BindFramebufferScope(FramebufferTarget.ReadFramebuffer, unchecked((int)id));
        var attachments = ImmutableDictionary.CreateBuilder<FramebufferAttachment, GpuFramebufferImageMetadata>();
        int limit = GpuSupport.Graphics.MaxColorAttachments;
        for (int index = 0; index < limit; index++)
            if (!ReadAttachment(id, named, FramebufferAttachment.ColorAttachment0 + index, attachments)) return false;
        if (!ReadAttachment(id, named, FramebufferAttachment.DepthAttachment, attachments)
            || !ReadAttachment(id, named, FramebufferAttachment.StencilAttachment, attachments)) return false;
        // Older contexts have no attachmentless state. Do not query unsupported tokens.
        bool defaults = GpuSupport.Graphics.SupportsFramebufferNoAttachments;
        int width = defaults ? ReadDefault(id, FramebufferDefaultParameter.FramebufferDefaultWidth) : 0;
        int height = defaults ? ReadDefault(id, FramebufferDefaultParameter.FramebufferDefaultHeight) : 0;
        int layers = defaults ? ReadDefault(id, FramebufferDefaultParameter.FramebufferDefaultLayers) : 0;
        int samples = defaults ? ReadDefault(id, FramebufferDefaultParameter.FramebufferDefaultSamples) : 0;
        int fixedSamples = defaults ? ReadDefault(id, FramebufferDefaultParameter.FramebufferDefaultFixedSampleLocations) : 0;
        if (width < 0 || height < 0 || layers < 0 || samples < 0 || fixedSamples < 0) return false;
        metadata = new(attachments.ToImmutable(), width, height, layers, samples, fixedSamples != 0);
        return true;
    }
    #endregion

    #region Private
    /// <summary>Reads one selected image without resolving a deleted retained name as a new allocation.</summary>
    private static bool ReadAttachment(nint id, bool named, FramebufferAttachment slot,
        ImmutableDictionary<FramebufferAttachment, GpuFramebufferImageMetadata>.Builder attachments)
    {
        int type = Read(id, named, slot, FramebufferParameterName.FramebufferAttachmentObjectType);
        if (type == (int)All.None) return true;
        int name = Read(id, named, slot, FramebufferParameterName.FramebufferAttachmentObjectName);
        if (name <= 0) return false;
        if (type == (int)All.Renderbuffer)
        {
            attachments.Add(slot, new(GpuResourceKind.Renderbuffer, name, 0, 0, 0, false));
            return true;
        }
        if (type != (int)All.Texture) return false;
        int level = Read(id, named, slot, FramebufferParameterName.FramebufferAttachmentTextureLevel);
        int layer = Read(id, named, slot, FramebufferParameterName.FramebufferAttachmentTextureLayer);
        int face = Read(id, named, slot, FramebufferParameterName.FramebufferAttachmentTextureCubeMapFace);
        int layered = Read(id, named, slot, FramebufferParameterName.FramebufferAttachmentLayered);
        bool multiview = GpuSupport.Graphics.SupportsOvrMultiview;
        int baseView = multiview ? Read(id, named, slot, (FramebufferParameterName)All.FramebufferAttachmentTextureBaseViewIndexOvr) : 0;
        int views = multiview ? Read(id, named, slot, (FramebufferParameterName)All.FramebufferAttachmentTextureNumViewsOvr) : 0;
        if (level < 0 || layer < 0 || face < 0 || layered < 0 || baseView < 0 || views < 0) return false;
        attachments.Add(slot, new(GpuResourceKind.Texture, name, level, layer, (TextureTarget)face, layered != 0, baseView, views));
        return true;
    }

    /// <summary>Uses an invalid sentinel to detect unavailable attachment facts without consuming errors.</summary>
    private static int Read(nint id, bool named, FramebufferAttachment slot, FramebufferParameterName parameter)
    {
        int value = -1;
        if (named)
            GL.GetNamedFramebufferAttachmentParameter(unchecked((int)id), slot, parameter, out value);
        else
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
