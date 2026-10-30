using VanillaGraphicsExpanded.Rendering.RHI;
using System.Collections.Immutable;

namespace VanillaGraphicsExpanded.Rendering;

/// <summary>Describes native attachments and attachmentless defaults without duplicating routing state.</summary>
public readonly record struct GpuFramebufferMetadata(
    ImmutableDictionary<FramebufferSlot, GpuFramebufferImageMetadata> Attachments,
    int DefaultWidth, int DefaultHeight, int DefaultLayers, int DefaultSamples, bool DefaultFixedSampleLocations){
    #region Public API
    /// <summary>Compares selected images and original backing identities independently of dictionary order.</summary>
    public bool Equals(GpuFramebufferMetadata other)
    {
        if (DefaultWidth != other.DefaultWidth || DefaultHeight != other.DefaultHeight || DefaultLayers != other.DefaultLayers
            || DefaultSamples != other.DefaultSamples || DefaultFixedSampleLocations != other.DefaultFixedSampleLocations) return false;
        if (Attachments is null || other.Attachments is null) return ReferenceEquals(Attachments, other.Attachments);
        if (Attachments.Count != other.Attachments.Count) return false;
        foreach (var pair in Attachments)
            if (!other.Attachments.TryGetValue(pair.Key, out var value) || pair.Value != value) return false;
        return true;
    }
    /// <summary>Hashes scalar defaults and unordered attachment selections.</summary>
    public override int GetHashCode()
    {
        int images = Attachments is null ? -1 : 0;
        if (Attachments is not null)
            foreach (var pair in Attachments) images ^= System.HashCode.Combine(pair.Key, pair.Value);
        return System.HashCode.Combine(DefaultWidth, DefaultHeight, DefaultLayers, DefaultSamples, DefaultFixedSampleLocations, images);
    }
    #endregion
}

/// <summary>Identifies the native image selected by an attachment; image facts resolve through its registration.</summary>
public readonly record struct GpuFramebufferImageMetadata(
    GpuResourceKind Kind, nint Id, int MipLevel, int Layer, CubeFace CubeFace,
    bool Layered, int BaseView = 0, int ViewCount = 0,
    GpuFramebufferAttachmentMode Mode = GpuFramebufferAttachmentMode.Unknown)
{
    /// <summary>Retains the original backing identity when its numeric name is later reused.</summary>
    internal GpuResourceRegistration? OriginalBacking { get; init; }
}

/// <summary>Distinguishes proven attachment command semantics from ambiguous native selection queries.</summary>
public enum GpuFramebufferAttachmentMode { Unknown, Ordinary, Multiview }
