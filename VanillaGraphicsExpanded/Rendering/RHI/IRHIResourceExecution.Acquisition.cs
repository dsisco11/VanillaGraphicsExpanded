using System;
using System.Collections.Generic;

namespace VanillaGraphicsExpanded.Rendering.RHI;

/// <summary>Defines manager-authorized native discovery independently of the concrete backend; never exposed to consumers.</summary>
internal partial interface IRHIResourceExecution
{
    #region Buffer and image acquisition
    /// <summary>Executes explicit manager-authorized acquisition without changing publication authority.</summary>
    bool IsExternalBuffer(nint id);
    /// <summary>Executes explicit manager-authorized acquisition without changing publication authority.</summary>
    bool TryReadBufferMetadata(nint id, out GpuBufferMetadata metadata);
    /// <summary>Executes explicit manager-authorized acquisition without changing publication authority.</summary>
    bool IsExternalTexture(nint id);
    /// <summary>Executes explicit manager-authorized acquisition without changing publication authority.</summary>
    bool ReadExternalTexture(nint id, TextureDimension target, out GpuTextureMetadata metadata);
    /// <summary>Executes explicit manager-authorized acquisition without changing publication authority.</summary>
    bool ReadExternalTextureImage(int id, TextureDimension target, CubeFace face, int level, out GpuTextureLevelMetadata image);
    /// <summary>Executes explicit manager-authorized acquisition without changing publication authority.</summary>
    bool IsExternalRenderbuffer(nint id);
    /// <summary>Executes explicit manager-authorized acquisition without changing publication authority.</summary>
    bool ReadExternalRenderbuffer(nint id, out GpuRenderbufferMetadata metadata);
    /// <summary>Executes explicit manager-authorized acquisition without changing publication authority.</summary>
    int ReadExternalTextureParameter(int texture, TextureMetadataField parameter);
    /// <summary>Executes explicit manager-authorized acquisition without changing publication authority.</summary>
    int ReadExternalTextureLevelParameter(int texture, int level, TextureMetadataField parameter);
    /// <summary>Executes explicit manager-authorized acquisition without changing publication authority.</summary>
    bool ReadExternalRenderbufferTexture(int texture, out GpuTextureMetadata metadata);
    #endregion
    #region Format capabilities
    /// <summary>Fills the existing target/format cache through admitted native capability discovery.</summary>
    InternalFormatCapabilities ReadFormatCapabilities(TextureDimension target, TextureFormat format, GpuResourceKind kind = GpuResourceKind.Texture);
    #endregion
    #region Executable acquisition
    /// <summary>Executes explicit manager-authorized acquisition without changing publication authority.</summary>
    bool ReadProgramMetadata(nint id, out GpuProgramMetadata metadata);
    /// <summary>Executes explicit manager-authorized acquisition without changing publication authority.</summary>
    bool ReadShaderMetadata(nint id, out GpuShaderMetadata metadata);
    #endregion
    #region Geometry admission
    /// <summary>Validates authored geometry/ranges without inspecting the external native layout.</summary>
    void ValidateImportedGeometry(int array, RHIVertexLayout layout, IReadOnlyList<RHIVertexStream> streams, RHIIndexStream indices);
    #endregion
}
