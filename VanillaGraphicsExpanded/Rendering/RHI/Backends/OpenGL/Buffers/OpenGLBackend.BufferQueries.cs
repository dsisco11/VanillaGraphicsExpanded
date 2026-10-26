using System;
using GL = OpenTK.Graphics.OpenGL4.GL;
using Native = OpenTK.Graphics.OpenGL4;
using Legacy = OpenTK.Graphics.OpenGL;

namespace VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;

/// <summary>Executes native reads only when the manager explicitly acquires unknown external facts.</summary>
internal sealed partial class OpenGLBackend
{
    #region Internal API
    /// <summary>Checks an unknown external object's existence at the explicit acquisition boundary.</summary>
    internal bool IsExternalBuffer(nint id)
    {
        RequireContext();
        bool live = id != 0 && id >= int.MinValue && id <= uint.MaxValue && GL.IsBuffer(unchecked((int)id));
        RequireContext();
        return live;
    }

    /// <summary>Acquires storage using named queries, preserving all surrounding selectors and native errors.</summary>
    internal unsafe bool TryReadBufferMetadata(nint id, out GpuBufferMetadata metadata)
    {
        metadata = default;
        if (!IsExternalBuffer(id)) return false;
        long capacity = -1;
        int usage = -1;
        int immutable = 0, flags = 0;
        GL.GetNamedBufferParameter(unchecked((int)id), Native.BufferParameterName.BufferSize, &capacity);
        RequireContext();
        GL.GetNamedBufferParameter(unchecked((int)id), Native.BufferParameterName.BufferUsage, &usage);
        RequireContext();
        GL.GetNamedBufferParameter(unchecked((int)id), Native.BufferParameterName.BufferImmutableStorage, &immutable);
        RequireContext();
        if (immutable != 0) GL.GetNamedBufferParameter(unchecked((int)id), Native.BufferParameterName.BufferStorageFlags, &flags);
        RequireContext();
        if (capacity < 0 || capacity > nint.MaxValue || !Enum.IsDefined((Legacy.BufferUsageHint)usage)) return false;
        metadata = new((nint)capacity, (Legacy.BufferUsageHint)usage, immutable != 0, (Legacy.BufferStorageFlags)flags);
        return true;
    }
    #endregion
}
