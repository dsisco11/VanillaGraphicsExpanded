using System;
using System.Collections.Generic;
using OpenTK.Graphics.OpenGL;

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
    bool ReadExternalTexture(nint id, TextureTarget target, out GpuTextureMetadata metadata);
    /// <summary>Executes explicit manager-authorized acquisition without changing publication authority.</summary>
    bool ReadExternalTextureImage(int id, TextureTarget target, TextureTarget face, int level, out GpuTextureLevelMetadata image);
    /// <summary>Executes explicit manager-authorized acquisition without changing publication authority.</summary>
    bool IsExternalRenderbuffer(nint id);
    /// <summary>Executes explicit manager-authorized acquisition without changing publication authority.</summary>
    bool ReadExternalRenderbuffer(nint id, out GpuRenderbufferMetadata metadata);
    /// <summary>Executes explicit manager-authorized acquisition without changing publication authority.</summary>
    int ReadExternalTextureParameter(int texture, GetTextureParameter parameter);
    /// <summary>Executes explicit manager-authorized acquisition without changing publication authority.</summary>
    int ReadExternalTextureLevelParameter(int texture, int level, GetTextureParameter parameter);
    /// <summary>Executes explicit manager-authorized acquisition without changing publication authority.</summary>
    bool ReadExternalRenderbufferTexture(int texture, out GpuTextureMetadata metadata);
    #endregion
    #region Format capabilities
    /// <summary>Fills the existing target/format cache through admitted native capability discovery.</summary>
    InternalFormatCapabilities ReadFormatCapabilities(ImageTarget target, SizedInternalFormat format);
    #endregion
    #region Executable acquisition
    /// <summary>Executes explicit manager-authorized acquisition without changing publication authority.</summary>
    bool IsProgramResource(nint id);
    /// <summary>Executes explicit manager-authorized acquisition without changing publication authority.</summary>
    bool ReadProgramMetadata(nint id, out GpuProgramMetadata metadata);
    /// <summary>Executes explicit manager-authorized acquisition without changing publication authority.</summary>
    bool IsShaderResource(nint id);
    /// <summary>Executes explicit manager-authorized acquisition without changing publication authority.</summary>
    bool ReadShaderMetadata(nint id, out GpuShaderMetadata metadata);
    #endregion
    #region Geometry certification
    /// <summary>Executes explicit manager-authorized acquisition without changing publication authority.</summary>
    void CertifyGeometryConfiguration(int array, RHIVertexLayout layout, IReadOnlyList<RHIVertexStream> streams, RHIIndexStream indices);
    #endregion
}
