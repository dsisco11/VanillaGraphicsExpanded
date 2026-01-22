using System;
using System.Diagnostics;

using OpenTK.Graphics.OpenGL;

namespace VanillaGraphicsExpanded.Rendering;

/// <summary>
/// RAII wrapper around an OpenGL vertex array object (VAO).
/// All methods require a current GL context on the calling thread.
/// </summary>
internal sealed class GpuVao : IDisposable
{
    private int vertexArrayId;
    private string? debugName;
    private bool isDisposed;

    public int VertexArrayId => vertexArrayId;
    public string? DebugName => debugName;

    public bool IsDisposed => isDisposed;
    public bool IsValid => vertexArrayId != 0 && !isDisposed;

    private GpuVao(int vertexArrayId, string? debugName)
    {
        this.vertexArrayId = vertexArrayId;
        this.debugName = debugName;
    }

    public static GpuVao Create(string? debugName = null)
    {
        int id = GL.GenVertexArray();
        if (id == 0)
        {
            throw new InvalidOperationException("glGenVertexArrays failed.");
        }

#if DEBUG
        GlDebug.TryLabel(ObjectLabelIdentifier.VertexArray, id, debugName);
#endif

        return new GpuVao(id, debugName);
    }

    public int Detach()
    {
        if (isDisposed)
        {
            return 0;
        }

        int id = vertexArrayId;
        vertexArrayId = 0;
        isDisposed = true;
        return id;
    }

    public int ReleaseHandle()
    {
        return Detach();
    }

    public void SetDebugName(string? debugName)
    {
        this.debugName = debugName;

#if DEBUG
        if (vertexArrayId != 0)
        {
            GlDebug.TryLabel(ObjectLabelIdentifier.VertexArray, vertexArrayId, debugName);
        }
#endif
    }

    public BindingScope BindScope()
    {
        int previous = 0;
        try
        {
            GL.GetInteger(GetPName.VertexArrayBinding, out previous);
        }
        catch
        {
            previous = 0;
        }

        Bind();
        return new BindingScope(previous);
    }

    public void Bind()
    {
        if (!IsValid)
        {
            Debug.WriteLine("[GpuVao] Attempted to bind disposed or invalid VAO");
            return;
        }

        GL.BindVertexArray(vertexArrayId);
    }

    public void Unbind()
    {
        GL.BindVertexArray(0);
    }

    public void BindElementBuffer(int bufferId)
    {
        if (!IsValid)
        {
            Debug.WriteLine("[GpuVao] Attempted to bind EBO on disposed or invalid VAO");
            return;
        }

        Bind();
        GL.BindBuffer(BufferTarget.ElementArrayBuffer, bufferId);
    }

    public void BindElementBuffer(GpuEbo ebo)
    {
        ArgumentNullException.ThrowIfNull(ebo);
        BindElementBuffer(ebo.BufferId);
    }

    public void UnbindElementBuffer()
    {
        BindElementBuffer(0);
    }

    public void EnableAttrib(int index)
    {
        if (!IsValid)
        {
            Debug.WriteLine("[GpuVao] Attempted to enable attrib on disposed or invalid VAO");
            return;
        }

        Bind();
        GL.EnableVertexAttribArray(index);
    }

    public void DisableAttrib(int index)
    {
        if (!IsValid)
        {
            Debug.WriteLine("[GpuVao] Attempted to disable attrib on disposed or invalid VAO");
            return;
        }

        Bind();
        GL.DisableVertexAttribArray(index);
    }

    public void AttribPointer(
        int index,
        int size,
        VertexAttribPointerType type,
        bool normalized,
        int strideBytes,
        int offsetBytes)
    {
        if (!IsValid)
        {
            Debug.WriteLine("[GpuVao] Attempted to set attrib pointer on disposed or invalid VAO");
            return;
        }

        Bind();
        GL.EnableVertexAttribArray(index);
        GL.VertexAttribPointer(index, size, type, normalized, strideBytes, offsetBytes);
    }

    public void AttribIPointer(
        int index,
        int size,
        VertexAttribIntegerType type,
        int strideBytes,
        int offsetBytes)
    {
        if (!IsValid)
        {
            Debug.WriteLine("[GpuVao] Attempted to set integer attrib pointer on disposed or invalid VAO");
            return;
        }

        Bind();
        GL.EnableVertexAttribArray(index);
        GL.VertexAttribIPointer(index, size, type, strideBytes, (IntPtr)offsetBytes);
    }

    public void AttribDivisor(int index, int divisor)
    {
        if (!IsValid)
        {
            Debug.WriteLine("[GpuVao] Attempted to set attrib divisor on disposed or invalid VAO");
            return;
        }

        Bind();
        GL.VertexAttribDivisor(index, divisor);
    }

    public void DrawElements(PrimitiveType primitiveType, DrawElementsType indexType, int indexCount, int offsetBytes = 0)
    {
        if (!IsValid)
        {
            return;
        }

        if (indexCount <= 0)
        {
            return;
        }

        if (offsetBytes < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(offsetBytes), offsetBytes, "Offset must be >= 0.");
        }

        Bind();
        GL.DrawElements(primitiveType, indexCount, indexType, (IntPtr)offsetBytes);
    }

    public void DrawElements(PrimitiveType primitiveType, GpuEbo ebo, int indexCount = 0, int offsetBytes = 0)
    {
        ArgumentNullException.ThrowIfNull(ebo);
        int count = indexCount > 0 ? indexCount : ebo.IndexCount;
        BindElementBuffer(ebo);
        DrawElements(primitiveType, ebo.IndexType, count, offsetBytes);
    }

    public void DrawElementsInstanced(
        PrimitiveType primitiveType,
        DrawElementsType indexType,
        int indexCount,
        int instanceCount,
        int offsetBytes = 0)
    {
        if (!IsValid)
        {
            return;
        }

        if (indexCount <= 0 || instanceCount <= 0)
        {
            return;
        }

        if (offsetBytes < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(offsetBytes), offsetBytes, "Offset must be >= 0.");
        }

        Bind();
        GL.DrawElementsInstanced(primitiveType, indexCount, indexType, (IntPtr)offsetBytes, instanceCount);
    }

    public void DrawElementsInstanced(
        PrimitiveType primitiveType,
        GpuEbo ebo,
        int instanceCount,
        int indexCount = 0,
        int offsetBytes = 0)
    {
        ArgumentNullException.ThrowIfNull(ebo);
        int count = indexCount > 0 ? indexCount : ebo.IndexCount;
        BindElementBuffer(ebo);
        DrawElementsInstanced(primitiveType, ebo.IndexType, count, instanceCount, offsetBytes);
    }

    public void Dispose()
    {
        if (isDisposed)
        {
            return;
        }

        if (vertexArrayId != 0)
        {
            GL.DeleteVertexArray(vertexArrayId);
            vertexArrayId = 0;
        }

        isDisposed = true;
    }

    public readonly struct BindingScope : IDisposable
    {
        private readonly int previous;

        public BindingScope(int previous)
        {
            this.previous = previous;
        }

        public void Dispose()
        {
            GL.BindVertexArray(previous);
        }
    }
}
