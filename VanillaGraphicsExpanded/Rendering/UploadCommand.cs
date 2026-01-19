namespace VanillaGraphicsExpanded.Rendering;

internal readonly record struct UploadCommand(
    long SequenceId,
    int Priority,
    UploadCommandKind Kind,
    TextureUploadRequest Request,
    int ByteCount,
    int RowLength,
    int ImageHeight,
    PboUpload PboUpload,
    MappedFlushRange FlushRange,
    OwnedCpuUploadBuffer? OwnedBuffer)
{
    public static UploadCommand FromCpu(long sequenceId, int priority, in TextureUploadRequest request)
        => new(
            sequenceId,
            priority,
            UploadCommandKind.FromCpu,
            request,
            ByteCount: 0,
            RowLength: 0,
            ImageHeight: 0,
            PboUpload: default,
            FlushRange: MappedFlushRange.None,
            OwnedBuffer: null);

    public static UploadCommand FromCpuPrepared(
        long sequenceId,
        int priority,
        in TextureUploadRequest request,
        int byteCount,
        int rowLength,
        int imageHeight)
        => new(
            sequenceId,
            priority,
            UploadCommandKind.FromCpu,
            request,
            byteCount,
            rowLength,
            imageHeight,
            PboUpload: default,
            FlushRange: MappedFlushRange.None,
            OwnedBuffer: null);

    public static UploadCommand FromPersistentRing(
        long sequenceId,
        int priority,
        in TextureUploadRequest request,
        int byteCount,
        int rowLength,
        int imageHeight,
        in PboUpload pboUpload,
        in MappedFlushRange flushRange)
        => new(
            sequenceId,
            priority,
            UploadCommandKind.FromPersistentRing,
            request,
            byteCount,
            rowLength,
            imageHeight,
            pboUpload,

            flushRange,
            OwnedBuffer: null);

    public static UploadCommand FromOwnedCpuBytes(
        long sequenceId,
        int priority,
        in TextureUploadRequest request,
        int byteCount,
        int rowLength,
        int imageHeight,
        OwnedCpuUploadBuffer owned)
        => new(
            sequenceId,
            priority,
            UploadCommandKind.FromOwnedCpuBytes,
            request,
            byteCount,
            rowLength,
            imageHeight,
            PboUpload: default,
            FlushRange: MappedFlushRange.None,
            OwnedBuffer: owned);
}
