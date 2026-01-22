using System;

using OpenTK.Graphics.OpenGL;

namespace VanillaGraphicsExpanded.Rendering;

/// <summary>
/// RAII wrapper around an OpenGL buffer intended for vertex data.
/// </summary>
internal sealed class GpuVbo : GpuBufferObject
{
    private GpuVbo(int bufferId, BufferTarget target, BufferUsageHint usage, string? debugName)
    {
        this.bufferId = bufferId;
        this.target = target;
        this.usage = usage;
        this.debugName = debugName;
    }

    public static GpuVbo Create(
        BufferTarget target = BufferTarget.ArrayBuffer,
        BufferUsageHint usage = BufferUsageHint.StaticDraw,
        string? debugName = null)
    {
        int id = CreateBufferId(debugName);
        return new GpuVbo(id, target, usage, debugName);
    }
}
