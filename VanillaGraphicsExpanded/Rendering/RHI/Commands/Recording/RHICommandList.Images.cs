using System;
namespace VanillaGraphicsExpanded.Rendering.RHI;
/// <summary>Retains images arguments without consulting executed state.</summary>
public sealed partial class RHICommandList
{
    private readonly CommandPages<UploadTexturePayload> payloadUploadTexture = new();
    /// <summary>Locates fully retained arguments in typed tables.</summary>
    private readonly record struct UploadTexturePayload(int destination, RHITextureRegion region, RHIPixelEncoding encoding, (int Offset, int Length) bytes, int rowPitch, int slicePitch);
    private readonly CommandPages<TransferBufferToTexturePayload> payloadTransferBufferToTexture = new();
    /// <summary>Locates fully retained arguments in typed tables.</summary>
    private readonly record struct TransferBufferToTexturePayload(int destination, int transfer);
    private readonly CommandPages<TransferTextureToBufferPayload> payloadTransferTextureToBuffer = new();
    /// <summary>Locates fully retained arguments in typed tables.</summary>
    private readonly record struct TransferTextureToBufferPayload(int source, int transfer);
    private readonly CommandPages<CopyTexturePayload> payloadCopyTexture = new();
    /// <summary>Locates fully retained arguments in typed tables.</summary>
    private readonly record struct CopyTexturePayload(int source, RHITextureRegion sourceRegion, int destination, RHITextureRegion destinationRegion);
    private readonly CommandPages<ClearTexturePayload> payloadClearTexture = new();
    /// <summary>Locates fully retained arguments in typed tables.</summary>
    private readonly record struct ClearTexturePayload(int destination, RHITextureRegion region, RHIPixelEncoding encoding, (int Offset, int Length) value);
    private readonly CommandPages<BlitImagesPayload> payloadBlitImages = new();
    /// <summary>Locates fully retained arguments in typed tables.</summary>
    private readonly record struct BlitImagesPayload(int blit);
    #region Public API
    /// <summary>Records upload texture with supplied inputs only; native validation remains at execution.</summary>
    public void UploadTexture(GpuTextureRef destination, RHITextureRegion region, RHIPixelEncoding encoding, ReadOnlySpan<byte> bytes, int rowPitch = 0, int slicePitch = 0)
    {
        RequireRecording();
        ShapeImage(destination, region); ShapePixels(region, encoding, bytes.Length, rowPitch, slicePitch);

        int payload = payloadUploadTexture.Add(new(textureReferencesArguments.Add(destination), region, encoding, this.bytes.Add(bytes), rowPitch, slicePitch));
        headers.Add(new(CommandOperation.UploadTexture, payload));
    }
    /// <summary>Records transfer buffer to texture with supplied inputs only; native validation remains at execution.</summary>
    public void TransferBufferToTexture(GpuTextureRef destination, RHITextureTransfer transfer)
    {
        RequireRecording();
        ShapeImage(destination, transfer.Region); ShapeBuffer(transfer.Buffer, transfer.Offset, 0);

        int payload = payloadTransferBufferToTexture.Add(new(textureReferencesArguments.Add(destination), textureTransferArguments.Add(transfer)));
        headers.Add(new(CommandOperation.TransferBufferToTexture, payload));
    }
    /// <summary>Records transfer texture to buffer with supplied inputs only; native validation remains at execution.</summary>
    public void TransferTextureToBuffer(GpuTextureRef source, RHITextureTransfer transfer)
    {
        RequireRecording();
        ShapeImage(source, transfer.Region); ShapeBuffer(transfer.Buffer, transfer.Offset, 0);

        int payload = payloadTransferTextureToBuffer.Add(new(textureReferencesArguments.Add(source), textureTransferArguments.Add(transfer)));
        headers.Add(new(CommandOperation.TransferTextureToBuffer, payload));
    }
    /// <summary>Records copy texture with supplied inputs only; native validation remains at execution.</summary>
    public void CopyTexture(GpuTextureRef source, RHITextureRegion sourceRegion, GpuTextureRef destination, RHITextureRegion destinationRegion)
    {
        RequireRecording();
        ShapeImage(source, sourceRegion); ShapeImage(destination, destinationRegion);

        int payload = payloadCopyTexture.Add(new(textureReferencesArguments.Add(source), sourceRegion, textureReferencesArguments.Add(destination), destinationRegion));
        headers.Add(new(CommandOperation.CopyTexture, payload));
    }
    /// <summary>Records clear texture with supplied inputs only; native validation remains at execution.</summary>
    public void ClearTexture(GpuTextureRef destination, RHITextureRegion region, RHIPixelEncoding encoding, ReadOnlySpan<byte> value)
    {
        RequireRecording();
        ShapeImage(destination, region); if (value.Length != encoding.BytesPerPixel) throw new ArgumentException("Supply one complete clear pixel.");

        int payload = payloadClearTexture.Add(new(textureReferencesArguments.Add(destination), region, encoding, bytes.Add(value)));
        headers.Add(new(CommandOperation.ClearTexture, payload));
    }
    /// <summary>Records blit images with supplied inputs only; native validation remains at execution.</summary>
    public void BlitImages(RHIImageBlit blit)
    {
        RequireRecording();
        operations |= BoundaryOperations.RenderPasses;

        int payload = payloadBlitImages.Add(new(imageBlitArguments.Add(blit)));
        headers.Add(new(CommandOperation.BlitImages, payload));
    }
    #endregion
    #region Internal API
    /// <summary>Interprets this operation family through the existing immediate context.</summary>
    internal void ExecuteImages(RHICommand command, RHICommandContext context)
    {
        switch (command.Operation)
        {
            case CommandOperation.UploadTexture:
            {
                var value = payloadUploadTexture.Get(command.Payload);
                context.UploadTexture(textureReferencesArguments.Get(value.destination), value.region, value.encoding, bytes.Read(value.bytes), value.rowPitch, value.slicePitch);

                return;
            }
            case CommandOperation.TransferBufferToTexture:
            {
                var value = payloadTransferBufferToTexture.Get(command.Payload);
                context.TransferBufferToTexture(textureReferencesArguments.Get(value.destination), textureTransferArguments.Get(value.transfer));

                return;
            }
            case CommandOperation.TransferTextureToBuffer:
            {
                var value = payloadTransferTextureToBuffer.Get(command.Payload);
                context.TransferTextureToBuffer(textureReferencesArguments.Get(value.source), textureTransferArguments.Get(value.transfer));

                return;
            }
            case CommandOperation.CopyTexture:
            {
                var value = payloadCopyTexture.Get(command.Payload);
                context.CopyTexture(textureReferencesArguments.Get(value.source), value.sourceRegion, textureReferencesArguments.Get(value.destination), value.destinationRegion);

                return;
            }
            case CommandOperation.ClearTexture:
            {
                var value = payloadClearTexture.Get(command.Payload);
                context.ClearTexture(textureReferencesArguments.Get(value.destination), value.region, value.encoding, bytes.Read(value.value));

                return;
            }
            case CommandOperation.BlitImages:
            {
                var value = payloadBlitImages.Get(command.Payload);
                context.BlitImages(imageBlitArguments.Get(value.blit));

                return;
            }
            default: throw new InvalidOperationException("Unexpected operation family.");
        }
    }
    #endregion
}
