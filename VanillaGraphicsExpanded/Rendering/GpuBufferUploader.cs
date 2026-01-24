using System;

using OpenTK.Graphics.OpenGL;

namespace VanillaGraphicsExpanded.Rendering;

internal static class GpuBufferUploader
{
    // 0 = unknown, 1 = enabled, -1 = disabled.
    private static int dsaEnabledState;

    public static bool BufferData(GpuBufferObject buffer, int byteCount, IntPtr data)
    {
        if (TryNamedBufferData(buffer.BufferId, byteCount, data, buffer.Usage))
        {
            return true;
        }

        using var scope = buffer.BindScope();

        if (byteCount == 0)
        {
            GL.BufferData(buffer.Target, IntPtr.Zero, IntPtr.Zero, buffer.Usage);
            return false;
        }

        GL.BufferData(buffer.Target, (IntPtr)byteCount, data, buffer.Usage);
        return false;
    }

    public static bool BufferSubData(GpuBufferObject buffer, int dstOffsetBytes, int byteCount, IntPtr data)
    {
        if (TryNamedBufferSubData(buffer.BufferId, dstOffsetBytes, byteCount, data))
        {
            return true;
        }

        using var scope = buffer.BindScope();
        GL.BufferSubData(buffer.Target, (IntPtr)dstOffsetBytes, (IntPtr)byteCount, data);
        return false;
    }

    private static bool TryNamedBufferData(int bufferId, int byteCount, IntPtr data, BufferUsageHint usage)
    {
        if (bufferId == 0 || dsaEnabledState == -1)
        {
            return false;
        }

        try
        {
            GL.NamedBufferData(bufferId, byteCount, data, usage);
            dsaEnabledState = 1;
            return true;
        }
        catch
        {
            dsaEnabledState = -1;
            return false;
        }
    }

    private static bool TryNamedBufferSubData(int bufferId, int dstOffsetBytes, int byteCount, IntPtr data)
    {
        if (bufferId == 0 || dsaEnabledState == -1)
        {
            return false;
        }

        try
        {
            GL.NamedBufferSubData(bufferId, (IntPtr)dstOffsetBytes, byteCount, data);
            dsaEnabledState = 1;
            return true;
        }
        catch
        {
            dsaEnabledState = -1;
            return false;
        }
    }
}

