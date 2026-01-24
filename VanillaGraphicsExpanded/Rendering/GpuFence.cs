using System;

using OpenTK.Graphics.OpenGL;

namespace VanillaGraphicsExpanded.Rendering;

/// <summary>
/// Small wrapper around an OpenGL sync object created by <c>glFenceSync</c>.
/// All methods require a current GL context on the calling thread.
/// </summary>
internal sealed class GpuFence : GpuResource, IDisposable
{
    private nint handle;

    protected override nint ResourceId
    {
        get => handle;
        set => handle = value;
    }

    protected override GpuResourceKind ResourceKind => GpuResourceKind.Sync;

    public new bool IsValid => handle != 0 && !IsDisposed;

    private GpuFence(nint handle)
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

        return new GpuFence((nint)sync);
    }

    public WaitSyncStatus Poll()
    {
        if (handle == 0)
        {
            return WaitSyncStatus.AlreadySignaled;
        }

        return GL.ClientWaitSync((IntPtr)handle, ClientWaitSyncFlags.None, 0);
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

    // Uses base GpuResource.Dispose() / deferred deletion via GpuResourceManager.
}
