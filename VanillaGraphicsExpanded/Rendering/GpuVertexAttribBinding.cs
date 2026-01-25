using System;
using System.Diagnostics;

using OpenTK.Graphics.OpenGL;

namespace VanillaGraphicsExpanded.Rendering;

/// <summary>
/// Helper wrapper for configuring a VAO vertex buffer binding + associated vertex attributes.
/// This is not an OpenGL object; it represents state stored inside a VAO.
/// </summary>
internal readonly struct GpuVertexAttribBinding
{
    private readonly int vaoId;
    private readonly int bindingIndex;

    private readonly int vboId;
    private readonly nint baseOffsetBytes;
    private readonly int strideBytes;
    private readonly int divisor;

    /// <summary>
    /// Gets the VAO id this binding config targets.
    /// </summary>
    public int VaoId => vaoId;

    /// <summary>
    /// Gets the binding index within the VAO.
    /// </summary>
    public int BindingIndex => bindingIndex;

    /// <summary>
    /// Gets the buffer id bound to this binding (if known; primarily for fallback paths).
    /// </summary>
    public int BufferId => vboId;

    /// <summary>
    /// Gets the base byte offset for this binding (if known; primarily for fallback paths).
    /// </summary>
    public nint BaseOffsetBytes => baseOffsetBytes;

    /// <summary>
    /// Gets the stride (in bytes) for this binding (if known; primarily for fallback paths).
    /// </summary>
    public int StrideBytes => strideBytes;

    /// <summary>
    /// Gets the instance divisor for this binding (0 for per-vertex).
    /// </summary>
    public int Divisor => divisor;

    public GpuVertexAttribBinding(int vaoId, int bindingIndex)
        : this(vaoId, bindingIndex, vboId: 0, baseOffsetBytes: 0, strideBytes: 0, divisor: 0)
    {
    }

    private GpuVertexAttribBinding(int vaoId, int bindingIndex, int vboId, nint baseOffsetBytes, int strideBytes, int divisor)
    {
        this.vaoId = vaoId;
        this.bindingIndex = bindingIndex;
        this.vboId = vboId;
        this.baseOffsetBytes = baseOffsetBytes;
        this.strideBytes = strideBytes;
        this.divisor = divisor;
    }

    /// <summary>
    /// Binds a vertex buffer to this binding index within the VAO.
    /// </summary>
    public GpuVertexAttribBinding BindVertexBuffer(int vboId, nint offsetBytes, int strideBytes)
    {
        if (vaoId == 0)
        {
            Debug.WriteLine("[GpuVertexAttribBinding] Attempted to configure binding on invalid VAO (0)");
            return this;
        }

        if (vboId == 0)
        {
            Debug.WriteLine("[GpuVertexAttribBinding] Attempted to bind invalid VBO id (0)");
            return this;
        }

        if (strideBytes < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(strideBytes), strideBytes, "Stride must be >= 0.");
        }

        if (!Dsa.TryVertexArrayVertexBuffer(vaoId, bindingIndex, vboId, offsetBytes, strideBytes))
        {
            // Legacy emulation: bind VAO + bind ARRAY_BUFFER. Offsets are applied per-attribute via VertexAttribPointer.
            GlStateCache.Current.BindVertexArray(vaoId);
            GlStateCache.Current.BindBuffer(BufferTarget.ArrayBuffer, vboId);
        }

        return new GpuVertexAttribBinding(vaoId, bindingIndex, vboId, offsetBytes, strideBytes, divisor);
    }

    /// <summary>
    /// Sets the instance divisor for this binding index (0 for per-vertex, 1 for per-instance, etc.).
    /// </summary>
    public GpuVertexAttribBinding SetDivisor(int divisor)
    {
        if (vaoId == 0)
        {
            return this;
        }

        if (divisor < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(divisor), divisor, "Divisor must be >= 0.");
        }

        if (!Dsa.TryVertexArrayBindingDivisor(vaoId, bindingIndex, divisor))
        {
            // Legacy emulation is per-attrib (VertexAttribDivisor) and is applied when configuring each attrib.
        }

        return new GpuVertexAttribBinding(vaoId, bindingIndex, vboId, baseOffsetBytes, strideBytes, divisor);
    }

    /// <summary>
    /// Configures a floating-point vertex attribute to read from this binding index.
    /// </summary>
    public void SetFloatAttrib(
        int attribIndex,
        int size,
        VertexAttribPointerType type,
        bool normalized,
        int relativeOffsetBytes)
    {
        if (vaoId == 0)
        {
            return;
        }

        if (relativeOffsetBytes < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(relativeOffsetBytes), relativeOffsetBytes, "Relative offset must be >= 0.");
        }

        if (Dsa.TryVertexArrayAttribFormatFloat(vaoId, attribIndex, size, type, normalized, relativeOffsetBytes)
            && Dsa.TryVertexArrayAttribBinding(vaoId, attribIndex, bindingIndex)
            && Dsa.TryEnableVertexArrayAttrib(vaoId, attribIndex))
        {
            if (divisor != 0)
            {
                Dsa.TryVertexArrayBindingDivisor(vaoId, bindingIndex, divisor);
            }

            return;
        }

        // Legacy emulation: requires a bound ARRAY_BUFFER.
        if (vboId == 0)
        {
            Debug.WriteLine("[GpuVertexAttribBinding] Legacy attrib pointer requires BindVertexBuffer() first");
            return;
        }

        nint absoluteOffset = baseOffsetBytes + relativeOffsetBytes;

        GlStateCache.Current.BindVertexArray(vaoId);
        GlStateCache.Current.BindBuffer(BufferTarget.ArrayBuffer, vboId);
        GL.EnableVertexAttribArray(attribIndex);
        GL.VertexAttribPointer(attribIndex, size, type, normalized, strideBytes, (IntPtr)absoluteOffset);
        if (divisor != 0)
        {
            GL.VertexAttribDivisor(attribIndex, divisor);
        }
    }

    /// <summary>
    /// Configures an integer vertex attribute to read from this binding index.
    /// </summary>
    public void SetIntAttrib(
        int attribIndex,
        int size,
        VertexAttribIntegerType type,
        int relativeOffsetBytes)
    {
        if (vaoId == 0)
        {
            return;
        }

        if (relativeOffsetBytes < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(relativeOffsetBytes), relativeOffsetBytes, "Relative offset must be >= 0.");
        }

        if (Dsa.TryVertexArrayAttribFormatInt(vaoId, attribIndex, size, type, relativeOffsetBytes)
            && Dsa.TryVertexArrayAttribBinding(vaoId, attribIndex, bindingIndex)
            && Dsa.TryEnableVertexArrayAttrib(vaoId, attribIndex))
        {
            if (divisor != 0)
            {
                Dsa.TryVertexArrayBindingDivisor(vaoId, bindingIndex, divisor);
            }

            return;
        }

        // Legacy emulation: requires a bound ARRAY_BUFFER.
        if (vboId == 0)
        {
            Debug.WriteLine("[GpuVertexAttribBinding] Legacy attrib pointer requires BindVertexBuffer() first");
            return;
        }

        nint absoluteOffset = baseOffsetBytes + relativeOffsetBytes;

        GlStateCache.Current.BindVertexArray(vaoId);
        GlStateCache.Current.BindBuffer(BufferTarget.ArrayBuffer, vboId);
        GL.EnableVertexAttribArray(attribIndex);
        GL.VertexAttribIPointer(attribIndex, size, type, strideBytes, (IntPtr)absoluteOffset);
        if (divisor != 0)
        {
            GL.VertexAttribDivisor(attribIndex, divisor);
        }
    }

    /// <summary>
    /// Disables a vertex attribute within this VAO.
    /// </summary>
    public void DisableAttrib(int attribIndex)
    {
        if (vaoId == 0)
        {
            return;
        }

        if (!Dsa.TryDisableVertexArrayAttrib(vaoId, attribIndex))
        {
            GlStateCache.Current.BindVertexArray(vaoId);
            GL.DisableVertexAttribArray(attribIndex);
        }
    }

    private static class Dsa
    {
        private static int enabledState;

        public static bool TryVertexArrayVertexBuffer(int vaoId, int bindingIndex, int vboId, nint offsetBytes, int strideBytes)
        {
            if (enabledState == -1)
            {
                return false;
            }

            try
            {
                GL.VertexArrayVertexBuffer(vaoId, bindingIndex, vboId, (IntPtr)offsetBytes, strideBytes);
                enabledState = 1;
                return true;
            }
            catch
            {
                enabledState = -1;
                return false;
            }
        }

        public static bool TryVertexArrayAttribBinding(int vaoId, int attribIndex, int bindingIndex)
        {
            if (enabledState == -1)
            {
                return false;
            }

            try
            {
                GL.VertexArrayAttribBinding(vaoId, attribIndex, bindingIndex);
                enabledState = 1;
                return true;
            }
            catch
            {
                enabledState = -1;
                return false;
            }
        }

        public static bool TryVertexArrayAttribFormatFloat(
            int vaoId,
            int attribIndex,
            int size,
            VertexAttribPointerType type,
            bool normalized,
            int relativeOffsetBytes)
        {
            if (enabledState == -1)
            {
                return false;
            }

            try
            {
                VertexAttribType attribType = (VertexAttribType)(int)type;
                GL.VertexArrayAttribFormat(vaoId, attribIndex, size, attribType, normalized, relativeOffsetBytes);
                enabledState = 1;
                return true;
            }
            catch
            {
                enabledState = -1;
                return false;
            }
        }

        public static bool TryVertexArrayAttribFormatInt(
            int vaoId,
            int attribIndex,
            int size,
            VertexAttribIntegerType type,
            int relativeOffsetBytes)
        {
            if (enabledState == -1)
            {
                return false;
            }

            try
            {
                VertexAttribIType attribType = (VertexAttribIType)(int)type;
                GL.VertexArrayAttribIFormat(vaoId, attribIndex, size, attribType, relativeOffsetBytes);
                enabledState = 1;
                return true;
            }
            catch
            {
                enabledState = -1;
                return false;
            }
        }

        public static bool TryEnableVertexArrayAttrib(int vaoId, int attribIndex)
        {
            if (enabledState == -1)
            {
                return false;
            }

            try
            {
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

        public static bool TryDisableVertexArrayAttrib(int vaoId, int attribIndex)
        {
            if (enabledState == -1)
            {
                return false;
            }

            try
            {
                GL.DisableVertexArrayAttrib(vaoId, attribIndex);
                enabledState = 1;
                return true;
            }
            catch
            {
                enabledState = -1;
                return false;
            }
        }

        public static bool TryVertexArrayBindingDivisor(int vaoId, int bindingIndex, int divisor)
        {
            if (enabledState == -1)
            {
                return false;
            }

            try
            {
                GL.VertexArrayBindingDivisor(vaoId, bindingIndex, divisor);
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
}
