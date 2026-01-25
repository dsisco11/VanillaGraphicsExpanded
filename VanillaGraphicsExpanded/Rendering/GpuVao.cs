using System;
using System.Diagnostics;

using OpenTK.Graphics.OpenGL;

namespace VanillaGraphicsExpanded.Rendering;

/// <summary>
/// RAII wrapper around an OpenGL vertex array object (VAO).
/// All methods require a current GL context on the calling thread.
/// </summary>
internal sealed class GpuVao : GpuResource, IDisposable
{
    private int vertexArrayId;
    private string? debugName;

    public int VertexArrayId => vertexArrayId;
    public string? DebugName => debugName;

    protected override nint ResourceId
    {
        get => vertexArrayId;
        set => vertexArrayId = (int)value;
    }

    protected override GpuResourceKind ResourceKind => GpuResourceKind.VertexArray;

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

    public override void SetDebugName(string? debugName)
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
        var scope = GlStateCache.Current.BindVertexArrayScope(vertexArrayId);
        return new BindingScope(scope);
    }

    public void Bind()
    {
        if (!IsValid)
        {
            Debug.WriteLine("[GpuVao] Attempted to bind disposed or invalid VAO");
            return;
        }

        GlStateCache.Current.BindVertexArray(vertexArrayId);
    }

    /// <summary>
    /// Gets a helper for configuring a VAO vertex buffer binding index.
    /// </summary>
    public GpuVertexAttribBinding GetBinding(int bindingIndex)
    {
        if (!IsValid)
        {
            Debug.WriteLine("[GpuVao] Attempted to get binding on disposed or invalid VAO");
            return default;
        }

        if (bindingIndex < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(bindingIndex), bindingIndex, "Binding index must be >= 0.");
        }

        return new GpuVertexAttribBinding(vertexArrayId, bindingIndex);
    }

    public bool TryBind()
    {
        if (!IsValid)
        {
            return false;
        }

        GlStateCache.Current.BindVertexArray(vertexArrayId);
        return true;
    }

    public void Unbind()
    {
        GlStateCache.Current.BindVertexArray(0);
    }

    public void BindElementBuffer(int bufferId)
    {
        if (!IsValid)
        {
            Debug.WriteLine("[GpuVao] Attempted to bind EBO on disposed or invalid VAO");
            return;
        }

        if (!VaoDsa.TryVertexArrayElementBuffer(vertexArrayId, bufferId))
        {
            Bind();
            GlStateCache.Current.BindBuffer(BufferTarget.ElementArrayBuffer, bufferId);
        }
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

        if (!VaoDsa.TryEnableVertexArrayAttrib(vertexArrayId, index))
        {
            Bind();
            GL.EnableVertexAttribArray(index);
        }
    }

    public void DisableAttrib(int index)
    {
        if (!IsValid)
        {
            Debug.WriteLine("[GpuVao] Attempted to disable attrib on disposed or invalid VAO");
            return;
        }

        if (!VaoDsa.TryDisableVertexArrayAttrib(vertexArrayId, index))
        {
            Bind();
            GL.DisableVertexAttribArray(index);
        }
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

        int vboId = 0;
        try
        {
            GL.GetInteger(GetPName.ArrayBufferBinding, out vboId);
        }
        catch
        {
            vboId = 0;
        }

        if (vboId != 0
            && strideBytes >= 0
            && offsetBytes >= 0
            && VaoDsa.TryAttribPointer(vertexArrayId, vboId, index, size, type, normalized, strideBytes, offsetBytes))
        {
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

        int vboId = 0;
        try
        {
            GL.GetInteger(GetPName.ArrayBufferBinding, out vboId);
        }
        catch
        {
            vboId = 0;
        }

        if (vboId != 0
            && strideBytes >= 0
            && offsetBytes >= 0
            && VaoDsa.TryAttribIPointer(vertexArrayId, vboId, index, size, type, strideBytes, offsetBytes))
        {
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

        if (!VaoDsa.TryAttribDivisor(vertexArrayId, index, divisor))
        {
            Bind();
            GL.VertexAttribDivisor(index, divisor);
        }
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
        if (!IsValid)
        {
            return;
        }

        ArgumentNullException.ThrowIfNull(ebo);

        int count = indexCount > 0 ? indexCount : ebo.IndexCount;
        if (count <= 0)
        {
            return;
        }

        if (offsetBytes < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(offsetBytes), offsetBytes, "Offset must be >= 0.");
        }

        Bind();
        GlStateCache.Current.BindBuffer(BufferTarget.ElementArrayBuffer, ebo.BufferId);
        GL.DrawElements(primitiveType, count, ebo.IndexType, (IntPtr)offsetBytes);
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
        if (!IsValid)
        {
            return;
        }

        ArgumentNullException.ThrowIfNull(ebo);

        int count = indexCount > 0 ? indexCount : ebo.IndexCount;
        if (count <= 0 || instanceCount <= 0)
        {
            return;
        }

        if (offsetBytes < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(offsetBytes), offsetBytes, "Offset must be >= 0.");
        }

        Bind();
        GlStateCache.Current.BindBuffer(BufferTarget.ElementArrayBuffer, ebo.BufferId);
        GL.DrawElementsInstanced(primitiveType, count, ebo.IndexType, (IntPtr)offsetBytes, instanceCount);
    }

    public override string ToString()
    {
        return $"{GetType().Name}(id={vertexArrayId}, name={debugName}, disposed={IsDisposed})";
    }

    private static class VaoDsa
    {
        private static int enabledState;

        public static bool TryVertexArrayElementBuffer(int vaoId, int bufferId)
        {
            if (enabledState == -1)
            {
                return false;
            }

            try
            {
                GL.VertexArrayElementBuffer(vaoId, bufferId);
                enabledState = 1;
                return true;
            }
            catch
            {
                enabledState = -1;
                return false;
            }
        }

        public static bool TryEnableVertexArrayAttrib(int vaoId, int index)
        {
            if (enabledState == -1)
            {
                return false;
            }

            try
            {
                GL.EnableVertexArrayAttrib(vaoId, index);
                enabledState = 1;
                return true;
            }
            catch
            {
                enabledState = -1;
                return false;
            }
        }

        public static bool TryDisableVertexArrayAttrib(int vaoId, int index)
        {
            if (enabledState == -1)
            {
                return false;
            }

            try
            {
                GL.DisableVertexArrayAttrib(vaoId, index);
                enabledState = 1;
                return true;
            }
            catch
            {
                enabledState = -1;
                return false;
            }
        }

        public static bool TryAttribPointer(
            int vaoId,
            int vboId,
            int attribIndex,
            int size,
            VertexAttribPointerType type,
            bool normalized,
            int strideBytes,
            int offsetBytes)
        {
            if (enabledState == -1)
            {
                return false;
            }

            try
            {
                GL.VertexArrayVertexBuffer(vaoId, attribIndex, vboId, (IntPtr)offsetBytes, strideBytes);
                VertexAttribType attribType = (VertexAttribType)(int)type;
                GL.VertexArrayAttribFormat(vaoId, attribIndex, size, attribType, normalized, 0);
                GL.VertexArrayAttribBinding(vaoId, attribIndex, attribIndex);
                GL.EnableVertexArrayAttrib(vaoId, attribIndex);

                enabledState = 1;
                return true;
            }
            catch
            {
                enabledState = -1;
                return false;
            }
        }

        public static bool TryAttribIPointer(
            int vaoId,
            int vboId,
            int attribIndex,
            int size,
            VertexAttribIntegerType type,
            int strideBytes,
            int offsetBytes)
        {
            if (enabledState == -1)
            {
                return false;
            }

            try
            {
                GL.VertexArrayVertexBuffer(vaoId, attribIndex, vboId, (IntPtr)offsetBytes, strideBytes);
                VertexAttribIType attribType = (VertexAttribIType)(int)type;
                GL.VertexArrayAttribIFormat(vaoId, attribIndex, size, attribType, 0);
                GL.VertexArrayAttribBinding(vaoId, attribIndex, attribIndex);
                GL.EnableVertexArrayAttrib(vaoId, attribIndex);

                enabledState = 1;
                return true;
            }
            catch
            {
                enabledState = -1;
                return false;
            }
        }

        public static bool TryAttribDivisor(int vaoId, int index, int divisor)
        {
            if (enabledState == -1)
            {
                return false;
            }

            try
            {
                GL.VertexArrayBindingDivisor(vaoId, index, divisor);

                enabledState = 1;
                return true;
            }
            catch
            {
                enabledState = -1;
                return false;
            }
        }

    }

    public readonly struct BindingScope : IDisposable
    {
        private readonly GlStateCache.VaoScope scope;

        public BindingScope(GlStateCache.VaoScope scope)
        {
            this.scope = scope;
        }

        public void Dispose()
        {
            scope.Dispose();
        }
    }
}
