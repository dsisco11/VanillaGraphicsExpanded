using System;

using OpenTK.Graphics.OpenGL;

namespace VanillaGraphicsExpanded.Rendering;

/// <summary>
/// RAII wrapper around an OpenGL buffer intended for vertex data.
/// </summary>
internal sealed class GpuVbo : GpuBufferObject
{
    /// <summary>Declares buffer policy before manager-owned native creation.</summary>
    private GpuVbo(BufferTarget target, BufferUsageHint usage, string? debugName)
    {
        this.target = target;
        allocationUsage = usage;
        this.debugName = debugName;
    }

    public static GpuVbo Create(
        BufferTarget target = BufferTarget.ArrayBuffer,
        BufferUsageHint usage = BufferUsageHint.StaticDraw,
        string? debugName = null)
    {
        var buffer = new GpuVbo(target, usage, debugName);
        buffer.InitializeNativeBuffer();
        return buffer;
    }
}
