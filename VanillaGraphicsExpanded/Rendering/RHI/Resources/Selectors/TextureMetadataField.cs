namespace VanillaGraphicsExpanded.Rendering.RHI;

/// <summary>Identifies logical TextureMetadataField selections independently of native API constants.</summary>
public enum TextureMetadataField
{
    /// <summary>Selects BaseMip.</summary>
    BaseMip,
    /// <summary>Selects MaxMip.</summary>
    MaxMip,
    /// <summary>Selects BufferObject.</summary>
    BufferObject,
    /// <summary>Selects InternalFormat.</summary>
    InternalFormat,
    /// <summary>Selects BufferOffset.</summary>
    BufferOffset,
    /// <summary>Selects BufferSize.</summary>
    BufferSize,
}
