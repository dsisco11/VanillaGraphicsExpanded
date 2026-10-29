using System;
using System.Collections.Generic;

namespace VanillaGraphicsExpanded.Rendering.RHI;

/// <summary>Admits manager-owned acquisition through the same execution boundary as public operations.</summary>
public sealed partial class RHICommandContext
{
    #region Internal API
    #region Buffer and image acquisition
    /// <summary>Routes explicit acquisition through the context execution contract.</summary>
    internal bool IsExternalBuffer(nint id) => backend.IsExternalBuffer(id);
    /// <summary>Routes explicit acquisition through the context execution contract.</summary>
    internal bool TryReadBufferMetadata(nint id, out GpuBufferMetadata metadata) => backend.TryReadBufferMetadata(id, out metadata);
    /// <summary>Routes explicit acquisition through the context execution contract.</summary>
    internal bool IsExternalTexture(nint id) => backend.IsExternalTexture(id);
    /// <summary>Routes explicit acquisition through the context execution contract.</summary>
    internal bool ReadExternalTexture(nint id, TextureDimension target, out GpuTextureMetadata metadata) => backend.ReadExternalTexture(id, target, out metadata);
    /// <summary>Routes explicit acquisition through the context execution contract.</summary>
    internal bool ReadExternalTextureImage(int id, TextureDimension target, CubeFace face, int level, out GpuTextureLevelMetadata image) => backend.ReadExternalTextureImage(id, target, face, level, out image);
    /// <summary>Routes explicit acquisition through the context execution contract.</summary>
    internal bool IsExternalRenderbuffer(nint id) => backend.IsExternalRenderbuffer(id);
    /// <summary>Routes explicit acquisition through the context execution contract.</summary>
    internal bool ReadExternalRenderbuffer(nint id, out GpuRenderbufferMetadata metadata) => backend.ReadExternalRenderbuffer(id, out metadata);
    /// <summary>Routes explicit acquisition through the context execution contract.</summary>
    internal int ReadExternalTextureParameter(int texture, TextureMetadataField parameter) => backend.ReadExternalTextureParameter(texture, parameter);
    /// <summary>Routes explicit acquisition through the context execution contract.</summary>
    internal int ReadExternalTextureLevelParameter(int texture, int level, TextureMetadataField parameter) => backend.ReadExternalTextureLevelParameter(texture, level, parameter);
    /// <summary>Routes explicit acquisition through the context execution contract.</summary>
    internal bool ReadExternalRenderbufferTexture(int texture, out GpuTextureMetadata metadata) => backend.ReadExternalRenderbufferTexture(texture, out metadata);
    #endregion
    #region Format capabilities
    /// <summary>Fills the existing capability cache through the execution contract, without another discovery authority.</summary>
    internal InternalFormatCapabilities ReadFormatCapabilities(TextureDimension target, TextureFormat format, GpuResourceKind kind = GpuResourceKind.Texture)
        => backend.ReadFormatCapabilities(target, format, kind);
    #endregion
    #region Executable acquisition
    /// <summary>Routes explicit acquisition through the context execution contract.</summary>
    internal bool ReadProgramMetadata(nint id, out GpuProgramMetadata metadata) => backend.ReadProgramMetadata(id, out metadata);
    /// <summary>Routes explicit acquisition through the context execution contract.</summary>
    internal bool ReadShaderMetadata(nint id, out GpuShaderMetadata metadata) => backend.ReadShaderMetadata(id, out metadata);
    #endregion
    #region Geometry certification
    /// <summary>Routes explicit acquisition through the context execution contract.</summary>
    internal bool CertifyGeometryConfiguration(int array, RHIVertexLayout layout, IReadOnlyList<RHIVertexStream> streams, RHIIndexStream indices, bool indexSelectedAtDraw) => backend.CertifyGeometryConfiguration(array, layout, streams, indices, indexSelectedAtDraw);
    #endregion
    #endregion
}
