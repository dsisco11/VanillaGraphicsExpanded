using System;
namespace VanillaGraphicsExpanded.Rendering.RHI;
/// <summary>Retains synchronization arguments without consulting executed state.</summary>
public sealed partial class RHICommandList
{
    private readonly CommandPages<BarrierPayload> payloadBarrier = new();
    /// <summary>Locates fully retained arguments in typed tables.</summary>
    private readonly record struct BarrierPayload(RHIBarrierDescription description);
    private readonly CommandPages<WaitGpuPayload> payloadWaitGpu = new();
    /// <summary>Locates fully retained arguments in typed tables.</summary>
    private readonly record struct WaitGpuPayload(int fence);
    private readonly CommandPages<InsertFencePayload> payloadInsertFence = new();
    /// <summary>Locates fully retained arguments in typed tables.</summary>
    private readonly record struct InsertFencePayload(int ticket);
    private readonly CommandPages<WaitSemaphorePayload> payloadWaitSemaphore = new();
    /// <summary>Locates fully retained arguments in typed tables.</summary>
    private readonly record struct WaitSemaphorePayload(int semaphore, (int Offset, int Length) buffers, (int Offset, int Length) textures);
    private readonly CommandPages<SignalSemaphorePayload> payloadSignalSemaphore = new();
    /// <summary>Locates fully retained arguments in typed tables.</summary>
    private readonly record struct SignalSemaphorePayload(int semaphore, (int Offset, int Length) buffers, (int Offset, int Length) textures);
    #region Public API
    /// <summary>Records barrier with supplied inputs only; native validation remains at execution.</summary>
    public void Barrier(RHIBarrierDescription description)
    {
        RequireRecording();

        
        int payload = payloadBarrier.Add(new(description));
        headers.Add(new(CommandOperation.Barrier, payload));
    }
    /// <summary>Records wait gpu with supplied inputs only; native validation remains at execution.</summary>
    public void WaitGpu(GpuSyncRef fence)
    {
        RequireRecording();
        if (fence.IsEmpty) throw new ArgumentException("A submitted fence is required.");

        int payload = payloadWaitGpu.Add(new(syncReferencesArguments.Add(fence)));
        headers.Add(new(CommandOperation.WaitGpu, payload));
    }
    /// <summary>Records insert fence with supplied inputs only; native validation remains at execution.</summary>
    public RHIFenceTicket InsertFence()
    {
        RequireRecording();

        var ticket = new RHIFenceTicket(origin); requests.Add(ticket);
        int payload = payloadInsertFence.Add(new(fenceTicketArguments.Add(ticket)));
        headers.Add(new(CommandOperation.InsertFence, payload));
        return ticket;
    }
    /// <summary>Records wait semaphore with supplied inputs only; native validation remains at execution.</summary>
    public void WaitSemaphore(GpuSemaphoreRef semaphore, ReadOnlySpan<GpuBufferRef> buffers, ReadOnlySpan<RHIExternalTextureTransition> textures)
    {
        RequireRecording();
        if (semaphore.IsEmpty) throw new ArgumentException("A semaphore is required.");

        int payload = payloadWaitSemaphore.Add(new(semaphoreReferencesArguments.Add(semaphore), semaphoreBuffers.Add(buffers), semaphoreTextures.Add(textures)));
        headers.Add(new(CommandOperation.WaitSemaphore, payload));
    }
    /// <summary>Records signal semaphore with supplied inputs only; native validation remains at execution.</summary>
    public void SignalSemaphore(GpuSemaphoreRef semaphore, ReadOnlySpan<GpuBufferRef> buffers, ReadOnlySpan<RHIExternalTextureTransition> textures)
    {
        RequireRecording();
        if (semaphore.IsEmpty) throw new ArgumentException("A semaphore is required.");

        int payload = payloadSignalSemaphore.Add(new(semaphoreReferencesArguments.Add(semaphore), semaphoreBuffers.Add(buffers), semaphoreTextures.Add(textures)));
        headers.Add(new(CommandOperation.SignalSemaphore, payload));
    }
    #endregion
    #region Internal API
    /// <summary>Interprets this operation family through the existing immediate context.</summary>
    internal void ExecuteSynchronization(RHICommand command, RHICommandContext context)
    {
        switch (command.Operation)
        {
            case CommandOperation.Barrier:
            {
                var value = payloadBarrier.Get(command.Payload);
                context.Barrier(value.description);

                return;
            }
            case CommandOperation.WaitGpu:
            {
                var value = payloadWaitGpu.Get(command.Payload);
                context.WaitGpu(syncReferencesArguments.Get(value.fence));

                return;
            }
            case CommandOperation.InsertFence:
            {
                var value = payloadInsertFence.Get(command.Payload);
                if (fenceTicketArguments.Get(value.ticket).Status == CompletionStatus.Pending) fenceTicketArguments.Get(value.ticket).Adopt(context, context.InsertFence());

                return;
            }
            case CommandOperation.WaitSemaphore:
            {
                var value = payloadWaitSemaphore.Get(command.Payload);
                context.WaitSemaphore(semaphoreReferencesArguments.Get(value.semaphore), semaphoreBuffers.Read(value.buffers), semaphoreTextures.Read(value.textures));

                return;
            }
            case CommandOperation.SignalSemaphore:
            {
                var value = payloadSignalSemaphore.Get(command.Payload);
                context.SignalSemaphore(semaphoreReferencesArguments.Get(value.semaphore), semaphoreBuffers.Read(value.buffers), semaphoreTextures.Read(value.textures));

                return;
            }
            default: throw new InvalidOperationException("Unexpected operation family.");
        }
    }
    #endregion
}
