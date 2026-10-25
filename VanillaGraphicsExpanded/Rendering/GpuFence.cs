using System;

using OpenTK.Graphics.OpenGL;

namespace VanillaGraphicsExpanded.Rendering;

/// <summary>
/// Small wrapper around an OpenGL sync object created by <c>glFenceSync</c>.
/// All methods require a current GL context on the calling thread.
/// </summary>
internal sealed class GpuFence : GpuResource<GpuSyncMetadata>, IDisposable
{
    private nint handle;
    private string? debugName;

    /// <inheritdoc />
    public override nint ResourceId
    {
        get => handle;
        protected set => handle = value;
    }

    protected override GpuResourceKind ResourceKind => GpuResourceKind.Sync;

    public new bool IsValid => base.IsValid;

    public override void SetDebugName(string? debugName)
    {
        // GLsync objects are not labelable via KHR_debug object labels in core GL.
        // Keep the name for debugging/logging only.
        this.debugName = debugName;
    }

    /// <summary>Declares an unpublished wrapper for manager-owned creation.</summary>
    private GpuFence() { }

    /// <summary>Inserts a fence through the manager without polling its status.</summary>
    public static GpuFence Insert()
    {
        var value = new GpuFence();
        try
        {
            if (!GpuResourceManager.CreateSync(new GpuSyncInitialization(), value, out _).HasValue)
                throw new InvalidOperationException("Sync creation failed.");
            return value;
        }
        catch
        {
            // The manager retires provisional allocation; dispose a completed handoff too.
            value.Dispose();
            throw;
        }
    }

    public WaitSyncStatus Poll()
    {
        if (handle == 0)
        {
            return WaitSyncStatus.AlreadySignaled;
        }

        return GL.ClientWaitSync((IntPtr)handle, ClientWaitSyncFlags.None, 0);
    }

    /// <summary>Waits for at most the supplied duration without consuming the fence; requires the owning context thread.</summary>
    public WaitSyncStatus Wait(TimeSpan timeout)
    {
        if (timeout < TimeSpan.Zero || timeout > TimeSpan.FromSeconds(60))
            throw new ArgumentOutOfRangeException(nameof(timeout));
        if (handle == 0) return WaitSyncStatus.AlreadySignaled;
        return GL.ClientWaitSync((IntPtr)handle, ClientWaitSyncFlags.SyncFlushCommandsBit,
            checked((long)(timeout.Ticks * 100)));
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
