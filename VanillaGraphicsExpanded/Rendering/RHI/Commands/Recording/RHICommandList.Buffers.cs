using System;
namespace VanillaGraphicsExpanded.Rendering.RHI;
/// <summary>Retains buffers arguments without consulting executed state.</summary>
public sealed partial class RHICommandList
{
    private readonly CommandPages<UploadBufferPayload> payloadUploadBuffer = new();
    /// <summary>Locates fully retained arguments in typed tables.</summary>
    private readonly record struct UploadBufferPayload(int destination, nint offset, (int Offset, int Length) bytes);
    private readonly CommandPages<CopyBufferPayload> payloadCopyBuffer = new();
    /// <summary>Locates fully retained arguments in typed tables.</summary>
    private readonly record struct CopyBufferPayload(int source, nint sourceOffset, int destination, nint destinationOffset, nint byteCount);
    private readonly CommandPages<ClearBufferPayload> payloadClearBuffer = new();
    /// <summary>Locates fully retained arguments in typed tables.</summary>
    private readonly record struct ClearBufferPayload(int destination, nint offset, nint byteCount, byte value);
    #region Public API
    /// <summary>Records upload buffer with supplied inputs only; native validation remains at execution.</summary>
    public void UploadBuffer(GpuBufferRef destination, nint offset, ReadOnlySpan<byte> bytes)
    {
        RequireRecording();
        ShapeBuffer(destination, offset, bytes.Length);

        int payload = payloadUploadBuffer.Add(new(bufferReferencesArguments.Add(destination), offset, this.bytes.Add(bytes)));
        headers.Add(new(CommandOperation.UploadBuffer, payload));
    }
    /// <summary>Records copy buffer with supplied inputs only; native validation remains at execution.</summary>
    public void CopyBuffer(GpuBufferRef source, nint sourceOffset, GpuBufferRef destination, nint destinationOffset, nint byteCount)
    {
        RequireRecording();
        ShapeBuffer(source, sourceOffset, byteCount); ShapeBuffer(destination, destinationOffset, byteCount);

        int payload = payloadCopyBuffer.Add(new(bufferReferencesArguments.Add(source), sourceOffset, bufferReferencesArguments.Add(destination), destinationOffset, byteCount));
        headers.Add(new(CommandOperation.CopyBuffer, payload));
    }
    /// <summary>Records clear buffer with supplied inputs only; native validation remains at execution.</summary>
    public void ClearBuffer(GpuBufferRef destination, nint offset, nint byteCount, byte value = 0)
    {
        RequireRecording();
        ShapeBuffer(destination, offset, byteCount);

        int payload = payloadClearBuffer.Add(new(bufferReferencesArguments.Add(destination), offset, byteCount, value));
        headers.Add(new(CommandOperation.ClearBuffer, payload));
    }
    #endregion
    #region Internal API
    /// <summary>Interprets this operation family through the existing immediate context.</summary>
    internal void ExecuteBuffers(RHICommand command, RHICommandContext context)
    {
        switch (command.Operation)
        {
            case CommandOperation.UploadBuffer:
            {
                var value = payloadUploadBuffer.Get(command.Payload);
                context.UploadBuffer(bufferReferencesArguments.Get(value.destination), value.offset, bytes.Read(value.bytes));

                return;
            }
            case CommandOperation.CopyBuffer:
            {
                var value = payloadCopyBuffer.Get(command.Payload);
                context.CopyBuffer(bufferReferencesArguments.Get(value.source), value.sourceOffset, bufferReferencesArguments.Get(value.destination), value.destinationOffset, value.byteCount);

                return;
            }
            case CommandOperation.ClearBuffer:
            {
                var value = payloadClearBuffer.Get(command.Payload);
                context.ClearBuffer(bufferReferencesArguments.Get(value.destination), value.offset, value.byteCount, value.value);

                return;
            }
            default: throw new InvalidOperationException("Unexpected operation family.");
        }
    }
    #endregion
}
