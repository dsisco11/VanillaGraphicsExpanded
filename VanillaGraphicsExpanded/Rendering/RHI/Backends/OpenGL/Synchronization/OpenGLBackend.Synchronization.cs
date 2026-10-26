using System;
using OpenTK.Graphics.OpenGL4;

namespace VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;

/// <summary>Executes synchronization once on the original admitted context.</summary>
internal sealed partial class OpenGLBackend
{
    #region Public API
    /// <inheritdoc />
    public void Barrier(in RHIBarrierDescription description)
    {
        RequireContext();
        if ((description.Access & ~RHIAccess.All) != 0) throw new ArgumentOutOfRangeException(nameof(description));
        MemoryBarrierFlags flags = 0;
        if (description.Access.HasFlag(RHIAccess.Vertex)) flags |= MemoryBarrierFlags.VertexAttribArrayBarrierBit;
        if (description.Access.HasFlag(RHIAccess.Index)) flags |= MemoryBarrierFlags.ElementArrayBarrierBit;
        if (description.Access.HasFlag(RHIAccess.Uniform)) flags |= MemoryBarrierFlags.UniformBarrierBit;
        if (description.Access.HasFlag(RHIAccess.TextureFetch)) flags |= MemoryBarrierFlags.TextureFetchBarrierBit;
        if (description.Access.HasFlag(RHIAccess.Image)) flags |= MemoryBarrierFlags.ShaderImageAccessBarrierBit;
        if (description.Access.HasFlag(RHIAccess.Indirect)) flags |= MemoryBarrierFlags.CommandBarrierBit;
        if (description.Access.HasFlag(RHIAccess.PixelTransfer)) flags |= MemoryBarrierFlags.PixelBufferBarrierBit;
        if (description.Access.HasFlag(RHIAccess.TextureUpdate)) flags |= MemoryBarrierFlags.TextureUpdateBarrierBit;
        if (description.Access.HasFlag(RHIAccess.BufferUpdate)) flags |= MemoryBarrierFlags.BufferUpdateBarrierBit;
        if (description.Access.HasFlag(RHIAccess.Framebuffer)) flags |= MemoryBarrierFlags.FramebufferBarrierBit;
        if (description.Access.HasFlag(RHIAccess.TransformFeedback)) flags |= MemoryBarrierFlags.TransformFeedbackBarrierBit;
        if (description.Access.HasFlag(RHIAccess.AtomicCounter)) flags |= MemoryBarrierFlags.AtomicCounterBarrierBit;
        if (description.Access.HasFlag(RHIAccess.ShaderStorage)) flags |= MemoryBarrierFlags.ShaderStorageBarrierBit;
        if (description.Access.HasFlag(RHIAccess.ClientMapped)) flags |= MemoryBarrierFlags.ClientMappedBufferBarrierBit;
        if (description.Access.HasFlag(RHIAccess.QueryResult)) flags |= MemoryBarrierFlags.QueryBufferBarrierBit;
        GL.MemoryBarrier(flags);
        RequireContext();
    }
    /// <inheritdoc />
    public GpuFence InsertFence()
    {
        RequireContext();
        var owner = new GpuFence();
        try
        {
            if (!GpuResourceManager.CreateSync(new(), owner, out _).HasValue)
                throw new InvalidOperationException("Fence insertion failed.");
            return owner;
        }
        catch { try { owner.Dispose(); } catch { } throw; }
    }
    /// <inheritdoc />
    public RHICompletionStatus WaitCpu(GpuSyncRef fence, TimeSpan timeout, bool flush)
    {
        RequireContext();
        if (timeout < TimeSpan.Zero || timeout > TimeSpan.FromSeconds(60)) throw new ArgumentOutOfRangeException(nameof(timeout));
        var owner = RequireSync(fence);
        var status = GL.ClientWaitSync(owner.ResourceId, flush ? ClientWaitSyncFlags.SyncFlushCommandsBit : ClientWaitSyncFlags.None,
            checked(timeout.Ticks * 100)) switch
        {
            WaitSyncStatus.AlreadySignaled or WaitSyncStatus.ConditionSatisfied => RHICompletionStatus.Ready,
            WaitSyncStatus.TimeoutExpired => RHICompletionStatus.Pending,
            _ => RHICompletionStatus.Failed
        };
        RequireContext();
        return status;
    }
    /// <inheritdoc />
    public void WaitGpu(GpuSyncRef fence)
    {
        var owner = RequireSync(fence);
        GL.WaitSync(owner.ResourceId, WaitSyncFlags.None, -1L);
        RequireContext();
    }
    /// <inheritdoc />
    public void Flush() { RequireContext(); GL.Flush(); RequireContext(); }
    /// <inheritdoc />
    public void WaitIdle() { RequireContext(); GL.Finish(); RequireContext(); }
    #endregion

    #region Internal API
    /// <summary>Creates a pointer-width candidate for the manager's private handoff, without status queries.</summary>
    internal GpuSyncMetadata? InitializeSync(out nint id)
    {
        RequireContext();
        id = GL.FenceSync(SyncCondition.SyncGpuCommandsComplete, WaitSyncFlags.None);
        RequireContext();
        return id == 0 ? null : new GpuSyncMetadata();
    }
    #endregion

    #region Private
    /// <summary>Rejects expired identity and foreign context before issuing a wait.</summary>
    private GpuFence RequireSync(GpuSyncRef fence)
    {
        RequireContext();
        var owner = fence.RequireResource();
        owner.RequireOriginalContext();
        if (!owner.IsValid) throw new InvalidOperationException("The original fence has retired.");
        cache.RequireResourceOperationAvailable(GpuResourceKind.Sync, owner.ResourceId);
        return owner;
    }
    #endregion
}
