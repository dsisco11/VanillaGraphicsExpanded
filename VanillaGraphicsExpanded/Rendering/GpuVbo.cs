using System;

using OpenTK.Graphics.OpenGL;

namespace VanillaGraphicsExpanded.Rendering;

/// <summary>
/// RAII wrapper around an OpenGL buffer intended for vertex data.
/// </summary>
internal sealed class GpuVbo : GpuBufferObject
{
    /// <summary>Retains allocation policy separately and acquires any realized native buffer facts.</summary>
    private GpuVbo(int bufferId, BufferTarget target, BufferUsageHint usage, string? debugName)
    {
        this.bufferId = bufferId;
        this.target = target;
        allocationUsage = usage;
        GpuResourceManager.RegisterBufferWrapper(this);
        this.debugName = debugName;
    }

    public static GpuVbo Create(
        BufferTarget target = BufferTarget.ArrayBuffer,
        BufferUsageHint usage = BufferUsageHint.StaticDraw,
        string? debugName = null)
    {
        int id = CreateBufferId(debugName);
        return CompleteOwnedCreation(GpuResourceKind.Buffer, id, () => new GpuVbo(id, target, usage, debugName));
    }
}
