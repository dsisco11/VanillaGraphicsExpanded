using System;
using OpenTK.Graphics.OpenGL;

namespace VanillaGraphicsExpanded.Rendering;

/// <summary>Executes buffer content uploads without publishing changes to allocation metadata.</summary>
internal sealed partial class StateCache
{
    #region Internal API
    /// <summary>Uploads bytes into the buffer bound to the wrapper's target.</summary>
    internal void BufferSubData(GpuBufferObject buffer, int offset, int byteCount, IntPtr data)
    {
        GL.BufferSubData(buffer.Target, (IntPtr)offset, (IntPtr)byteCount, data);
    }

    /// <summary>Uploads bytes directly into a named buffer without changing bindings.</summary>
    internal void NamedBufferSubData(int bufferId, int offset, int byteCount, IntPtr data)
    {
        GL.NamedBufferSubData(bufferId, (IntPtr)offset, byteCount, data);
    }
    #endregion
}
