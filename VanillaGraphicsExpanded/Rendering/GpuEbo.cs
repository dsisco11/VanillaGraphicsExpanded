using System;

using OpenTK.Graphics.OpenGL;

namespace VanillaGraphicsExpanded.Rendering;

/// <summary>
/// RAII wrapper around an OpenGL element/index buffer object (EBO / IBO).
/// All methods require a current GL context on the calling thread.
/// </summary>
internal sealed class GpuEbo : GpuBufferObject
{
    private int indexCount;
    private DrawElementsType indexType;

    public int IndexCount => indexCount;
    public DrawElementsType IndexType => indexType;

    private GpuEbo(int bufferId, BufferUsageHint usage, string? debugName)
    {
        this.bufferId = bufferId;
        target = BufferTarget.ElementArrayBuffer;
        this.usage = usage;
        this.debugName = debugName;
    }

    public static GpuEbo Create(BufferUsageHint usage = BufferUsageHint.StaticDraw, string? debugName = null)
    {
        int id = CreateBufferId(debugName);
        return new GpuEbo(id, usage, debugName);
    }

    public void UploadIndices(uint[] indices)
    {
        ArgumentNullException.ThrowIfNull(indices);

        UploadData(indices);
        indexCount = indices.Length;
        indexType = DrawElementsType.UnsignedInt;
    }

    public void UploadIndices(ushort[] indices)
    {
        ArgumentNullException.ThrowIfNull(indices);

        UploadData(indices);
        indexCount = indices.Length;
        indexType = DrawElementsType.UnsignedShort;
    }

    public void UploadIndices(byte[] indices)
    {
        ArgumentNullException.ThrowIfNull(indices);

        UploadData(indices);
        indexCount = indices.Length;
        indexType = DrawElementsType.UnsignedByte;
    }

    public void DrawElements(PrimitiveType primitiveType, int indexCount = 0, int offsetBytes = 0)
    {
        if (!IsValid)
        {
            return;
        }

        if (offsetBytes < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(offsetBytes), offsetBytes, "Offset must be >= 0.");
        }

        int count = indexCount > 0 ? indexCount : this.indexCount;
        if (count <= 0)
        {
            return;
        }

        GL.DrawElements(primitiveType, count, indexType, (IntPtr)offsetBytes);
    }

    public void DrawElementsInstanced(PrimitiveType primitiveType, int instanceCount, int indexCount = 0, int offsetBytes = 0)
    {
        if (!IsValid)
        {
            return;
        }

        if (instanceCount <= 0)
        {
            return;
        }

        if (offsetBytes < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(offsetBytes), offsetBytes, "Offset must be >= 0.");
        }

        int count = indexCount > 0 ? indexCount : this.indexCount;
        if (count <= 0)
        {
            return;
        }

        GL.DrawElementsInstanced(primitiveType, count, indexType, (IntPtr)offsetBytes, instanceCount);
    }

    public override int Detach()
    {
        int id = base.Detach();
        indexCount = 0;
        indexType = default;
        return id;
    }

    public override void Dispose()
    {
        base.Dispose();
        indexCount = 0;
        indexType = default;
    }
}
