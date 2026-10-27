using System;
using System.Collections.Generic;
using OpenTK.Graphics.OpenGL;

namespace VanillaGraphicsExpanded.Rendering.RHI;

/// <summary>Admits private resource initialization through the same execution boundary as public operations.</summary>
public sealed partial class RHICommandContext
{
    #region Internal API
    #region Buffer and image allocation
    /// <summary>Routes private initialization through the context execution contract.</summary>
    internal GpuBufferMetadata? InitializeBuffer(in GpuBufferInitialization initialization, out nint id, nint data = 0, int byteCount = 0, nint offset = 0) => backend.InitializeBuffer(initialization, out id, data, byteCount, offset);
    /// <summary>Routes private initialization through the context execution contract.</summary>
    internal GpuTextureMetadata? InitializeTexture(in GpuTextureInitialization request, out nint id, RHITextureInitialData[]? initialData = null) => backend.InitializeTexture(request, out id, initialData);
    /// <summary>Routes private initialization through the context execution contract.</summary>
    internal GpuTextureMetadata? InitializeBufferTexture(in GpuBufferTextureInitialization request, out nint id) => backend.InitializeBufferTexture(request, out id);
    /// <summary>Routes private initialization through the context execution contract.</summary>
    internal GpuTextureMetadata? InitializeTextureView(in GpuTextureViewInitialization request, GpuTextureMetadata source, out nint id) => backend.InitializeTextureView(request, source, out id);
    /// <summary>Routes private initialization through the context execution contract.</summary>
    internal GpuRenderbufferMetadata? InitializeRenderbuffer(in GpuRenderbufferInitialization request, out nint id) => backend.InitializeRenderbuffer(request, out id);
    /// <summary>Routes private initialization through the context execution contract.</summary>
    internal GpuSamplerMetadata? InitializeSampler(out nint id) => backend.InitializeSampler(out id);
    #endregion
    #region Executable allocation
    /// <summary>Routes private initialization through the context execution contract.</summary>
    internal GpuShaderMetadata? InitializeShader(in GpuShaderInitialization initialization, out nint id) => backend.InitializeShader(initialization, out id);
    /// <summary>Routes private initialization through the context execution contract.</summary>
    internal GpuProgramMetadata? InitializeProgram(out nint id) => backend.InitializeProgram(out id);
    /// <summary>Routes private initialization through the context execution contract.</summary>
    internal GpuProgramPipelineMetadata? InitializeProgramPipeline(out nint id) => backend.InitializeProgramPipeline(out id);
    /// <summary>Routes private initialization through the context execution contract.</summary>
    internal GpuVertexArrayMetadata? CreateVertexArray(out nint id) => backend.CreateVertexArray(out id);
    /// <summary>Routes private initialization through the context execution contract.</summary>
    internal GpuFramebufferMetadata? CreateFramebuffer(in GpuFramebufferInitialization request, out nint id) => backend.CreateFramebuffer(request, out id);
    #endregion
    #region Auxiliary allocation
    /// <summary>Routes private initialization through the context execution contract.</summary>
    internal GpuQueryMetadata? InitializeQuery(QueryKind kind, out nint id) => backend.InitializeQuery(kind, out id);
    /// <summary>Routes private initialization through the context execution contract.</summary>
    internal GpuTransformFeedbackMetadata? InitializeTransformFeedback(out nint id) => backend.InitializeTransformFeedback(out id);
    /// <summary>Routes private initialization through the context execution contract.</summary>
    internal GpuSyncMetadata? InitializeSync(out nint id) => backend.InitializeSync(out id);
    /// <summary>Routes private initialization through the context execution contract.</summary>
    internal GpuSemaphoreMetadata? InitializeSemaphore(out nint id) => backend.InitializeSemaphore(out id);
    /// <summary>Routes private initialization through the context execution contract.</summary>
    internal GpuMemoryObjectMetadata? InitializeMemoryObject(out nint id) => backend.InitializeMemoryObject(out id);
    #endregion
    #endregion
}
