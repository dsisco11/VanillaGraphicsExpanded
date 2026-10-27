using System;
using VanillaGraphicsExpanded.Rendering.RHI;

using OpenTK.Graphics.OpenGL;

namespace VanillaGraphicsExpanded.Rendering;

/// <summary>
/// RAII wrapper around an OpenGL buffer intended for vertex data.
/// </summary>
internal sealed class GpuVbo : GpuBufferObject
{
    #region Internal API
    /// <summary>Declares vertex-buffer policy without realizing a native resource.</summary>
    internal GpuVbo(BufferRole role, BufferUsage usage, string? debugName)
    {
        Role = role;
        allocationUsage = usage;
        this.debugName = debugName;
    }
    #endregion
}
