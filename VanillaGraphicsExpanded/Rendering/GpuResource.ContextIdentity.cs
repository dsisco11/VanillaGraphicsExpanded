using System;
using VanillaGraphicsExpanded.Rendering.Integration;

namespace VanillaGraphicsExpanded.Rendering;

/// <summary>Preserves original context identity for native resource use and cleanup.</summary>
public abstract partial class GpuResource
{
    #region Internal API
    /// <summary>Supplies the original namespace for backend-owned CPU configuration guards.</summary>
    internal (nint Handle, long Generation) OriginalContext => nativeContext;
    /// <summary>Captures registered identity, or the physical context for an explicitly local buffer or image tool.</summary>
    internal static (nint Handle, long Generation) CaptureCreationContext(GpuResourceKind kind)
    {
        // Owner policy selects which declarations may retain a physical context without a registered service.
        return kind is GpuResourceKind.Buffer or GpuResourceKind.Texture or GpuResourceKind.Renderbuffer
            or GpuResourceKind.Sampler or GpuResourceKind.Sync or GpuResourceKind.Query or GpuResourceKind.TransformFeedback
            or GpuResourceKind.Semaphore or GpuResourceKind.MemoryObject or GpuResourceKind.Shader or GpuResourceKind.Program
            or GpuResourceKind.Framebuffer or GpuResourceKind.VertexArray or GpuResourceKind.ProgramPipeline
            ? RenderContextRegistry.CaptureResourceContext() : RenderContextRegistry.Current();
    }

    /// <summary>Rejects resource use in a foreign namespace without native metadata discovery.</summary>
    internal void RequireOriginalContext()
    {
        if (!RenderContextRegistry.IsCurrentResourceContext(nativeContext))
            throw new InvalidOperationException("The resource's original context is required.");
    }

    #endregion
}
