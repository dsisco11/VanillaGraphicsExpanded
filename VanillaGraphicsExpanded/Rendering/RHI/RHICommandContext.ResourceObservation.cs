using OpenTK.Graphics.OpenGL;

namespace VanillaGraphicsExpanded.Rendering.RHI;

/// <summary>Routes manager-authorized observation through the admitted execution contract.</summary>
public sealed partial class RHICommandContext
{
    #region Internal API
    /// <summary>Requests explicit acquisition without exposing the concrete backend.</summary>
    internal bool IsFramebufferResource(nint id) => backend.IsFramebufferResource(id);
    /// <summary>Requests explicit acquisition without exposing the concrete backend.</summary>
    internal bool ReadFramebufferMetadata(nint id, out GpuFramebufferMetadata metadata) => backend.ReadFramebufferMetadata(id, out metadata);
    /// <summary>Requests explicit acquisition without exposing the concrete backend.</summary>
    internal bool ReadFramebufferField(nint id, GpuFramebufferMetadata previous, FramebufferAttachment? slot, FramebufferDefaultParameter? parameter, out GpuFramebufferMetadata metadata) => backend.ReadFramebufferField(id, previous, slot, parameter, out metadata);
    /// <summary>Requests explicit acquisition without exposing the concrete backend.</summary>
    internal int ReadExternalProgramUnit(int program, int location) => backend.ReadExternalProgramUnit(program, location);
    /// <summary>Requests explicit acquisition without exposing the concrete backend.</summary>
    internal bool ReadExternalProgramBlockBinding(int program, ProgramInterface block, int index, out int value) => backend.ReadExternalProgramBlockBinding(program, block, index, out value);
    /// <summary>Requests explicit acquisition without exposing the concrete backend.</summary>
    internal RHIGeometryContext CaptureGeometryContext() => backend.CaptureGeometryContext();
    #endregion
}
