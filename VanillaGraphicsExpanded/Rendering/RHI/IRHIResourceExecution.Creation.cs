using System;
using System.Collections.Generic;
using OpenTK.Graphics.OpenGL;

namespace VanillaGraphicsExpanded.Rendering.RHI;

/// <summary>Defines the private allocation handoff independently of the concrete backend; never exposed to consumers.</summary>
internal partial interface IRHIResourceExecution
{
    #region Buffer and image allocation
    /// <summary>Executes unpublished allocation without publishing or constructing another owner.</summary>
    GpuBufferMetadata? InitializeBuffer(in GpuBufferInitialization initialization, out nint id, nint data = 0, int byteCount = 0, nint offset = 0);
    /// <summary>Executes unpublished allocation without publishing or constructing another owner.</summary>
    GpuTextureMetadata? InitializeTexture(in GpuTextureInitialization request, out nint id, RHITextureInitialData[]? initialData = null);
    /// <summary>Executes unpublished allocation without publishing or constructing another owner.</summary>
    GpuTextureMetadata? InitializeBufferTexture(in GpuBufferTextureInitialization request, out nint id);
    /// <summary>Executes unpublished allocation without publishing or constructing another owner.</summary>
    GpuTextureMetadata? InitializeTextureView(in GpuTextureViewInitialization request, GpuTextureMetadata source, out nint id);
    /// <summary>Executes unpublished allocation without publishing or constructing another owner.</summary>
    GpuRenderbufferMetadata? InitializeRenderbuffer(in GpuRenderbufferInitialization request, out nint id);
    /// <summary>Executes unpublished allocation without publishing or constructing another owner.</summary>
    GpuSamplerMetadata? InitializeSampler(out nint id);
    #endregion
    #region Executable allocation
    /// <summary>Executes unpublished allocation without publishing or constructing another owner.</summary>
    GpuShaderMetadata? InitializeShader(in GpuShaderInitialization initialization, out nint id);
    /// <summary>Executes unpublished allocation without publishing or constructing another owner.</summary>
    GpuProgramMetadata? InitializeProgram(out nint id);
    /// <summary>Executes unpublished allocation without publishing or constructing another owner.</summary>
    GpuProgramPipelineMetadata? InitializeProgramPipeline(out nint id);
    /// <summary>Executes unpublished allocation without publishing or constructing another owner.</summary>
    GpuVertexArrayMetadata? CreateVertexArray(out nint id);
    /// <summary>Executes unpublished allocation without publishing or constructing another owner.</summary>
    GpuFramebufferMetadata? CreateFramebuffer(in GpuFramebufferInitialization request, out nint id);
    #endregion
    #region Auxiliary allocation
    /// <summary>Executes unpublished allocation without publishing or constructing another owner.</summary>
    GpuQueryMetadata? InitializeQuery(QueryKind kind, out nint id);
    /// <summary>Executes unpublished allocation without publishing or constructing another owner.</summary>
    GpuTransformFeedbackMetadata? InitializeTransformFeedback(out nint id);
    /// <summary>Executes unpublished allocation without publishing or constructing another owner.</summary>
    GpuSyncMetadata? InitializeSync(out nint id);
    /// <summary>Executes unpublished allocation without publishing or constructing another owner.</summary>
    GpuSemaphoreMetadata? InitializeSemaphore(out nint id);
    /// <summary>Executes unpublished allocation without publishing or constructing another owner.</summary>
    GpuMemoryObjectMetadata? InitializeMemoryObject(out nint id);
    #endregion
}
