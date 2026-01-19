using System;
using System.Buffers;

namespace VanillaGraphicsExpanded.Rendering;

internal sealed class OwnedCpuUploadBuffer : IDisposable
{
    private IMemoryOwner<byte>? owner;

    public OwnedCpuUploadBuffer(IMemoryOwner<byte> owner, int byteCount)
    {
        this.owner = owner ?? throw new ArgumentNullException(nameof(owner));
        ByteCount = byteCount;
    }

    public int ByteCount { get; }

    public Memory<byte> Memory
    {
        get
        {
            IMemoryOwner<byte>? o = owner;
            if (o is null)
            {
                return Memory<byte>.Empty;
            }

            return o.Memory.Slice(0, ByteCount);
        }
    }

    public void Dispose()
    {
        IMemoryOwner<byte>? o = owner;
        owner = null;
        o?.Dispose();
    }
}
