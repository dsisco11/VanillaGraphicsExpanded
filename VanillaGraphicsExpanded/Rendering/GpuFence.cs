using System;

using OpenTK.Graphics.OpenGL;

namespace VanillaGraphicsExpanded.Rendering;

/// <summary>
/// Small wrapper around an OpenGL sync object created by <c>glFenceSync</c>.
/// All methods require a current GL context on the calling thread.
/// </summary>
internal sealed class GpuFence : IDisposable
{
    private IntPtr handle;

    public bool IsValid => handle != IntPtr.Zero;

    private GpuFence(IntPtr handle)
    {
        this.handle = handle;
    }

    public static GpuFence Insert()
    {
        IntPtr sync = GL.FenceSync(SyncCondition.SyncGpuCommandsComplete, WaitSyncFlags.None);
        if (sync == IntPtr.Zero)
        {
            throw new InvalidOperationException("glFenceSync failed.");
        }

        return new GpuFence(sync);
    }

    public WaitSyncStatus Poll()
    {
        if (handle == IntPtr.Zero)
        {
            return WaitSyncStatus.AlreadySignaled;
        }

        return GL.ClientWaitSync(handle, ClientWaitSyncFlags.None, 0);
    }

    public bool TryConsumeIfSignaled()
    {
        WaitSyncStatus status = Poll();
        if (status == WaitSyncStatus.TimeoutExpired)
        {
            return false;
        }

        Dispose();
        return true;
    }

    public void Dispose()
    {
        if (handle == IntPtr.Zero)
        {
            return;
        }

        GL.DeleteSync(handle);
        handle = IntPtr.Zero;
    }
}

