using System;
using OpenTK.Graphics.OpenGL;

namespace VanillaGraphicsExpanded.Rendering;

/// <summary>Identifies the transient binding facts needed by a vertex configuration helper.</summary>
[Flags]
internal enum GpuVertexBindingFields { None = 0, Buffer = 1, Offset = 2, Stride = 4, Divisor = 8, All = 15 }

/// <summary>Acquires selected binding facts without creating a canonical VAO layout mirror.</summary>
internal static class GpuVertexBindingReader
{
    #region Internal API
    /// <summary>Reads only requested unknown fields, retaining pointer-width offsets.</summary>
    internal static void Read(int vao, int index, GpuVertexBindingFields fields,
        ref int buffer, ref nint offset, ref int stride, ref int divisor)
    {
        // Invalid sentinels distinguish failed native queries from valid zero-valued facts.
        if ((fields & GpuVertexBindingFields.Buffer) != 0)
            buffer = ReadInteger(vao, index, (VertexArrayIndexedParameter)All.VertexBindingBuffer);
        if ((fields & GpuVertexBindingFields.Offset) != 0)
        {
            long value = -1;
            GL.GetVertexArrayIndexed64(vao, index, VertexArrayIndexed64Parameter.VertexBindingOffset, out value);
            if (value < 0) throw new InvalidOperationException("The selected vertex offset could not be acquired.");
            offset = checked((nint)value);
        }
        if ((fields & GpuVertexBindingFields.Stride) != 0)
            stride = ReadInteger(vao, index, (VertexArrayIndexedParameter)All.VertexBindingStride);
        if ((fields & GpuVertexBindingFields.Divisor) != 0)
            divisor = ReadInteger(vao, index, (VertexArrayIndexedParameter)All.VertexBindingDivisor);
    }
    #endregion

    #region Private
    /// <summary>Acquires one nonnegative integer field while preserving native error state.</summary>
    private static int ReadInteger(int vao, int index, VertexArrayIndexedParameter parameter)
    {
        int value = -1;
        GL.GetVertexArrayIndexed(vao, index, parameter, out value);
        if (value < 0) throw new InvalidOperationException("The selected vertex binding fact could not be acquired.");
        return value;
    }
    #endregion
}
