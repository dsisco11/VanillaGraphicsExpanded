using System;
using System.Diagnostics;

using OpenTK.Graphics.OpenGL;

namespace VanillaGraphicsExpanded.Rendering;

/// <summary>
/// RAII wrapper around an OpenGL vertex array object (VAO).
/// All methods require a current GL context on the calling thread.
/// </summary>
internal sealed class GpuVao : GpuResource<GpuVertexArrayMetadata>, IDisposable
{
    private int vertexArrayId;
    private string? debugName;

    public int VertexArrayId => vertexArrayId;
    public string? DebugName => debugName;

    /// <inheritdoc />
    public override nint ResourceId
    {
        get => vertexArrayId;
        protected set => vertexArrayId = (int)value;
    }

    protected override GpuResourceKind ResourceKind => GpuResourceKind.VertexArray;

    private GpuVao(int vertexArrayId, string? debugName)
    {
        this.vertexArrayId = vertexArrayId;
        GpuResourceManager.RegisterEmptyMetadata(this);
        this.debugName = debugName;
    }

    /// <summary>Creates a realized vertex array suitable for bound and direct configuration.</summary>
    public static GpuVao Create(string? debugName = null)
    {
        int id;
        if (GpuSupport.Graphics.SupportsArbDirectStateAccess) GL.CreateVertexArrays(1, out id);
        else id = GL.GenVertexArray();
        if (id == 0)
        {
            throw new InvalidOperationException("glGenVertexArrays failed.");
        }

        return CompleteOwnedCreation(GpuResourceKind.VertexArray, id, () =>
        {
            // Generated names become objects on first binding. Restore the caller's VAO.
            if (!GpuSupport.Graphics.SupportsArbDirectStateAccess)
            {
                using var scope = StateCache.Current.BindVertexArrayScope(id);
            }
#if DEBUG
            GlDebug.TryLabel(ObjectLabelIdentifier.VertexArray, id, debugName);
#endif
            return new GpuVao(id, debugName);
        });
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

    /// <summary>Binds this live allocation and restores the previous binding when the scope ends.</summary>
    public BindingScope BindScope()
    {
        ObjectDisposedException.ThrowIf(!IsValid, this);
        var scope = StateCache.Current.BindVertexArrayScope(vertexArrayId);
        return new BindingScope(scope);
    }

    public void Bind()
    {
        if (!IsValid)
        {
            Debug.WriteLine("[GpuVao] Attempted to bind disposed or invalid VAO");
            return;
        }

        StateCache.Current.BindVertexArray(vertexArrayId);
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

        return new GpuVertexAttribBinding(this, bindingIndex);
    }

    public bool TryBind()
    {
        if (!IsValid)
        {
            return false;
        }

        StateCache.Current.BindVertexArray(vertexArrayId);
        return true;
    }

    public void Unbind()
    {
        StateCache.Current.BindVertexArray(0);
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
            StateCache.Current.BindBuffer(BufferTarget.ElementArrayBuffer, bufferId);
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
            StateCache.Current.EnableVertexAttribArray(index);
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
            StateCache.Current.DisableVertexAttribArray(index);
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
        StateCache.Current.EnableVertexAttribArray(index);
        StateCache.Current.VertexAttribPointer(index, size, type, normalized, strideBytes, offsetBytes);
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
        StateCache.Current.EnableVertexAttribArray(index);
        StateCache.Current.VertexAttribIPointer(index, size, type, strideBytes, (IntPtr)offsetBytes);
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
            StateCache.Current.VertexAttribDivisor(index, divisor);
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
        StateCache.Current.RecordDrawSubmission();
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
        StateCache.Current.BindBuffer(BufferTarget.ElementArrayBuffer, ebo.BufferId);
        GL.DrawElements(primitiveType, count, ebo.IndexType, (IntPtr)offsetBytes);
        StateCache.Current.RecordDrawSubmission();
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
        StateCache.Current.RecordDrawSubmission();
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
        StateCache.Current.BindBuffer(BufferTarget.ElementArrayBuffer, ebo.BufferId);
        GL.DrawElementsInstanced(primitiveType, count, ebo.IndexType, (IntPtr)offsetBytes, instanceCount);
        StateCache.Current.RecordDrawSubmission();
    }

    public override string ToString()
    {
        return $"{GetType().Name}(id={vertexArrayId}, name={debugName}, disposed={IsDisposed})";
    }

    /// <summary>Selects named VAO commands using the current context's shared capabilities.</summary>
    private static class VaoDsa
    {

        /// <summary>Uses the named vertex API when supported; native and publication failures propagate.</summary>
        public static bool TryVertexArrayElementBuffer(int vaoId, int bufferId)
        {
            if (!GpuSupport.Graphics.SupportsArbDirectStateAccess)
            {
                return false;
            }

            StateCache.Current.VertexArrayElementBuffer(vaoId, bufferId);
            return true;
        }

        /// <summary>Uses the named vertex API when supported; native and publication failures propagate.</summary>
        public static bool TryEnableVertexArrayAttrib(int vaoId, int index)
        {
            if (!GpuSupport.Graphics.SupportsArbDirectStateAccess)
            {
                return false;
            }

            StateCache.Current.EnableVertexArrayAttrib(vaoId, index);
            return true;
        }

        /// <summary>Uses the named vertex API when supported; native and publication failures propagate.</summary>
        public static bool TryDisableVertexArrayAttrib(int vaoId, int index)
        {
            if (!GpuSupport.Graphics.SupportsArbDirectStateAccess)
            {
                return false;
            }

            StateCache.Current.DisableVertexArrayAttrib(vaoId, index);
            return true;
        }

        /// <summary>Uses the named vertex API when supported; native and publication failures propagate.</summary>
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
            if (!GpuSupport.Graphics.SupportsArbDirectStateAccess)
            {
                return false;
            }

            StateCache.Current.VertexArrayVertexBuffer(vaoId, attribIndex, vboId, (IntPtr)offsetBytes, strideBytes);
            VertexAttribType attribType = (VertexAttribType)(int)type;
            StateCache.Current.VertexArrayAttribFormat(vaoId, attribIndex, size, attribType, normalized, 0);
            StateCache.Current.VertexArrayAttribBinding(vaoId, attribIndex, attribIndex);
            StateCache.Current.EnableVertexArrayAttrib(vaoId, attribIndex);
            return true;
        }

        /// <summary>Uses the named vertex API when supported; native and publication failures propagate.</summary>
        public static bool TryAttribIPointer(
            int vaoId,
            int vboId,
            int attribIndex,
            int size,
            VertexAttribIntegerType type,
            int strideBytes,
            int offsetBytes)
        {
            if (!GpuSupport.Graphics.SupportsArbDirectStateAccess)
            {
                return false;
            }

            StateCache.Current.VertexArrayVertexBuffer(vaoId, attribIndex, vboId, (IntPtr)offsetBytes, strideBytes);
            VertexAttribIType attribType = (VertexAttribIType)(int)type;
            StateCache.Current.VertexArrayAttribIFormat(vaoId, attribIndex, size, attribType, 0);
            StateCache.Current.VertexArrayAttribBinding(vaoId, attribIndex, attribIndex);
            StateCache.Current.EnableVertexArrayAttrib(vaoId, attribIndex);
            return true;
        }

        /// <summary>Uses the named vertex API when supported; native and publication failures propagate.</summary>
        public static bool TryAttribDivisor(int vaoId, int index, int divisor)
        {
            if (!GpuSupport.Graphics.SupportsArbDirectStateAccess)
            {
                return false;
            }

            StateCache.Current.VertexArrayBindingDivisor(vaoId, index, divisor);
            return true;
        }

    }

    public readonly struct BindingScope : IDisposable
    {
        private readonly StateCache.VaoScope scope;

        public BindingScope(StateCache.VaoScope scope)
        {
            this.scope = scope;
        }

        public void Dispose()
        {
            scope.Dispose();
        }
    }
}
