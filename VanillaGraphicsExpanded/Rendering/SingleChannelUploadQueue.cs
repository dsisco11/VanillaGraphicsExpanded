using System;
using System.Threading;
using System.Threading.Channels;

namespace VanillaGraphicsExpanded.Rendering;

internal sealed class SingleChannelUploadQueue : IUploadCommandQueue
{
    private readonly Channel<UploadCommand> channel;
    private readonly SemaphoreSlim enqueueSlots;
    private int count;

    public SingleChannelUploadQueue(int capacity)
    {
        if (capacity <= 0)
        {
            capacity = 1;
        }

        enqueueSlots = new SemaphoreSlim(capacity, capacity);

        channel = Channel.CreateBounded<UploadCommand>(new BoundedChannelOptions(capacity)
        {
            SingleReader = true,
            SingleWriter = false,
            FullMode = BoundedChannelFullMode.Wait
        });
    }

    public int Count => Volatile.Read(ref count);

    public bool TryAcquireEnqueueSlot(out UploadEnqueueToken token)
    {
        if (!enqueueSlots.Wait(0))
        {
            token = default;
            return false;
        }

        token = new UploadEnqueueToken(Reserved: 1);
        return true;
    }

    public bool TryEnqueue(in UploadEnqueueToken token, in UploadCommand command)
    {
        if (token.Reserved == 0)
        {
            return false;
        }

        if (!channel.Writer.TryWrite(command))
        {
            return false;
        }

        Interlocked.Increment(ref count);
        return true;
    }

    public void ReleaseEnqueueSlot(in UploadEnqueueToken token)
    {
        if (token.Reserved != 0)
        {
            enqueueSlots.Release();
        }
    }

    public int Drain(Span<UploadCommand> destination)
    {
        int drained = 0;

        while (drained < destination.Length && channel.Reader.TryRead(out UploadCommand cmd))
        {
            destination[drained++] = cmd;
            enqueueSlots.Release();
            Interlocked.Decrement(ref count);
        }

        return drained;
    }
}
