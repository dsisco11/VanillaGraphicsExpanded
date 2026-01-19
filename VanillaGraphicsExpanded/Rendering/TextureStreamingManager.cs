using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading;

using OpenTK.Graphics.OpenGL;

namespace VanillaGraphicsExpanded.Rendering;

internal sealed class TextureStreamingManager : IDisposable
{
    private readonly PriorityFifoQueue<TextureUploadRequest> pending = new();
    private readonly List<TextureUploadRequest> deferred = new();
    private TextureStreamingSettings settings;
    private IPboUploadBackend? backend;
    private bool backendInitialized;
    private int backendResetRequested;
    private bool disposed;

    private long enqueued;
    private long uploaded;
    private long uploadedBytes;
    private long fallbackUploads;
    private long droppedInvalid;
    private long deferredCount;

    public TextureStreamingManager(TextureStreamingSettings? settings = null)
    {
        this.settings = settings ?? TextureStreamingSettings.Default;
    }

    public TextureStreamingSettings Settings
    {
        get => settings;
        set => UpdateSettings(value);
    }

    public void UpdateSettings(TextureStreamingSettings value)
    {
        TextureStreamingSettings previous = settings;
        settings = value;

        if (RequiresBackendReset(previous, value))
        {
            RequestBackendReset();
        }
    }

    public void RequestBackendReset()
    {
        Interlocked.Exchange(ref backendResetRequested, 1);
    }

    public void Enqueue(TextureUploadRequest request)
    {
        pending.Enqueue(request.Priority, request);
        Interlocked.Increment(ref enqueued);
    }

    public void TickOnRenderThread()
    {
        if (disposed)
        {
            return;
        }

        if (Interlocked.Exchange(ref backendResetRequested, 0) != 0)
        {
            if (backend is not null)
            {
                GL.Finish();
                backend.Dispose();
                backend = null;
            }

            backendInitialized = false;
        }

        backend?.Tick();

        if (pending.Count == 0)
        {
            return;
        }

        EnsureBackend();

        int uploads = 0;
        int bytes = 0;

        while (uploads < settings.MaxUploadsPerFrame && bytes < settings.MaxBytesPerFrame)
        {
            if (!pending.TryDequeue(out TextureUploadRequest request))
            {
                break;
            }

            if (!TryPrepareRequest(request, out PreparedUpload prepared))
            {
                Interlocked.Increment(ref droppedInvalid);
                continue;
            }

            int byteCount = prepared.ByteCount;
            bool uploadedThisFrame = false;

            if (backend is not null && byteCount <= settings.MaxStagingBytes)
            {
                if (backend.TryStageUpload(prepared, out PboUpload pboUpload))
                {
                    IssuePboUpload(prepared, pboUpload);
                    backend.SubmitUpload(pboUpload);
                    uploadedThisFrame = true;
                }
            }

            if (!uploadedThisFrame)
            {
                if (settings.AllowDirectUploads && TryUploadDirect(prepared))
                {
                    Interlocked.Increment(ref fallbackUploads);
                    uploadedThisFrame = true;
                }
            }

            if (!uploadedThisFrame)
            {
                deferred.Add(request);
                Interlocked.Increment(ref deferredCount);
                break;
            }

            uploads++;
            bytes += byteCount;
            Interlocked.Increment(ref uploaded);
            Interlocked.Add(ref uploadedBytes, byteCount);
        }

        if (deferred.Count > 0)
        {
            foreach (TextureUploadRequest request in deferred)
            {
                pending.Enqueue(request.Priority, request);
            }

            deferred.Clear();
        }
    }

    public TextureStreamingDiagnostics GetDiagnosticsSnapshot()
    {
        return new TextureStreamingDiagnostics(
            Enqueued: Interlocked.Read(ref enqueued),
            Uploaded: Interlocked.Read(ref uploaded),
            UploadedBytes: Interlocked.Read(ref uploadedBytes),
            FallbackUploads: Interlocked.Read(ref fallbackUploads),
            DroppedInvalid: Interlocked.Read(ref droppedInvalid),
            Deferred: Interlocked.Read(ref deferredCount),
            Pending: pending.Count,
            Backend: backend?.Kind ?? TextureStreamingBackendKind.None);
    }

    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        backend?.Dispose();
        backend = null;
        disposed = true;
    }

    private void EnsureBackend()
    {
        if (backendInitialized)
        {
            return;
        }

        backendInitialized = true;

        if (!settings.EnablePboStreaming)
        {
            return;
        }

        if (!settings.ForceDisablePersistent && TextureStreamingUtils.SupportsBufferStorage())
        {
            try
            {
                backend = new PersistentMappedPboRing(settings.PersistentRingBytes, settings.PboAlignment, settings.UseCoherentMapping);
                return;
            }
            catch
            {
                backend = null;
            }
        }

        backend = new TripleBufferedPboPool(settings.TripleBufferBytes);
    }

    private static bool RequiresBackendReset(in TextureStreamingSettings previous, in TextureStreamingSettings next)
    {
        return previous.EnablePboStreaming != next.EnablePboStreaming
            || previous.ForceDisablePersistent != next.ForceDisablePersistent
            || previous.UseCoherentMapping != next.UseCoherentMapping
            || previous.PersistentRingBytes != next.PersistentRingBytes
            || previous.TripleBufferBytes != next.TripleBufferBytes
            || previous.PboAlignment != next.PboAlignment;
    }

    private static bool TryPrepareRequest(TextureUploadRequest request, out PreparedUpload prepared)
    {
        prepared = default;

        if (request.TextureId <= 0 || request.Data.IsEmpty)
        {
            return false;
        }

        TextureUploadRegion region = request.Region;
        if (region.Width <= 0 || region.Height <= 0 || region.Depth <= 0)
        {
            return false;
        }

        int rowLength = request.UnpackRowLength > 0 ? request.UnpackRowLength : region.Width;
        int imageHeight = request.UnpackImageHeight > 0 ? request.UnpackImageHeight : region.Height;
        if (rowLength <= 0 || imageHeight <= 0)
        {
            return false;
        }
        if (rowLength < region.Width || imageHeight < region.Height)
        {
            return false;
        }

        int bytesPerPixel = TextureStreamingUtils.GetBytesPerPixel(request.PixelFormat, request.PixelType);
        if (bytesPerPixel <= 0)
        {
            return false;
        }

        long byteCountLong = (long)rowLength * imageHeight * region.Depth * bytesPerPixel;
        if (byteCountLong <= 0 || byteCountLong > int.MaxValue)
        {
            return false;
        }

        int byteCount = (int)byteCountLong;
        if (request.Data.ByteLength < byteCount)
        {
            return false;
        }

        prepared = new PreparedUpload(request, byteCount, rowLength, imageHeight);
        return true;
    }
    private static void IssuePboUpload(in PreparedUpload prepared, in PboUpload pboUpload)
    {
        TextureUploadRequest request = prepared.Request;

        GL.BindTexture(request.Target.BindTarget, request.TextureId);
        GL.BindBuffer(BufferTarget.PixelUnpackBuffer, pboUpload.BufferId);

        ApplyPixelStore(prepared);
        try
        {
            UploadSubImage(prepared, new IntPtr(pboUpload.OffsetBytes));
        }
        finally
        {
            ResetPixelStore(prepared);
            GL.BindBuffer(BufferTarget.PixelUnpackBuffer, 0);
            GL.BindTexture(request.Target.BindTarget, 0);
        }
    }

    private static bool TryUploadDirect(in PreparedUpload prepared)
    {
        try
        {
            UploadDirect(prepared);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static unsafe void UploadDirect(in PreparedUpload prepared)
    {
        TextureUploadRequest request = prepared.Request;
        ApplyPixelStore(prepared);

        try
        {
            GL.BindTexture(request.Target.BindTarget, request.TextureId);

            switch (request.Data.Kind)
            {
                case TextureUploadDataKind.Bytes:
                {
                    byte[] data = (byte[])request.Data.DataArray;
                    fixed (byte* ptr = data)
                    {
                        UploadSubImage(prepared, new IntPtr(ptr));
                    }
                    break;
                }
                case TextureUploadDataKind.UShorts:
                {
                    ushort[] data = (ushort[])request.Data.DataArray;
                    fixed (ushort* ptr = data)
                    {
                        UploadSubImage(prepared, new IntPtr(ptr));
                    }
                    break;
                }
                case TextureUploadDataKind.Halfs:
                {
                    Half[] data = (Half[])request.Data.DataArray;
                    fixed (Half* ptr = data)
                    {
                        UploadSubImage(prepared, new IntPtr(ptr));
                    }
                    break;
                }
                case TextureUploadDataKind.Floats:
                {
                    float[] data = (float[])request.Data.DataArray;
                    fixed (float* ptr = data)
                    {
                        UploadSubImage(prepared, new IntPtr(ptr));
                    }
                    break;
                }
                default:
                    throw new InvalidOperationException("Unsupported upload data kind.");
            }
        }
        finally
        {
            GL.BindTexture(request.Target.BindTarget, 0);
            ResetPixelStore(prepared);
        }
    }

    private static void ApplyPixelStore(in PreparedUpload prepared)
    {
        TextureUploadRequest request = prepared.Request;
        int alignment = request.UnpackAlignment > 0 ? request.UnpackAlignment : 4;

        GL.PixelStore(PixelStoreParameter.UnpackAlignment, alignment);

        if (prepared.RowLength != request.Region.Width)
        {
            GL.PixelStore(PixelStoreParameter.UnpackRowLength, prepared.RowLength);
        }
        else
        {
            GL.PixelStore(PixelStoreParameter.UnpackRowLength, 0);
        }

        if (prepared.ImageHeight != request.Region.Height)
        {
            GL.PixelStore(PixelStoreParameter.UnpackImageHeight, prepared.ImageHeight);
        }
        else
        {
            GL.PixelStore(PixelStoreParameter.UnpackImageHeight, 0);
        }
    }

    private static void ResetPixelStore(in PreparedUpload prepared)
    {
        TextureUploadRequest request = prepared.Request;
        if (request.UnpackAlignment > 0 && request.UnpackAlignment != 4)
        {
            GL.PixelStore(PixelStoreParameter.UnpackAlignment, 4);
        }

        if (prepared.RowLength != request.Region.Width)
        {
            GL.PixelStore(PixelStoreParameter.UnpackRowLength, 0);
        }

        if (prepared.ImageHeight != request.Region.Height)
        {
            GL.PixelStore(PixelStoreParameter.UnpackImageHeight, 0);
        }
    }

    private static void UploadSubImage(in PreparedUpload prepared, IntPtr dataPtr)
    {
        TextureUploadRequest request = prepared.Request;
        TextureUploadRegion region = request.Region;
        TextureTarget target = request.Target.UploadTarget;

        switch (TextureStreamingUtils.GetUploadDimension(target))
        {
            case UploadDimension.Tex1D:
                GL.TexSubImage1D(
                    target,
                    region.MipLevel,
                    region.X,
                    region.Width,
                    request.PixelFormat,
                    request.PixelType,
                    dataPtr);
                break;
            case UploadDimension.Tex3D:
                GL.TexSubImage3D(
                    target,
                    region.MipLevel,
                    region.X,
                    region.Y,
                    region.Z,
                    region.Width,
                    region.Height,
                    region.Depth,
                    request.PixelFormat,
                    request.PixelType,
                    dataPtr);
                break;
            default:
                GL.TexSubImage2D(
                    target,
                    region.MipLevel,
                    region.X,
                    region.Y,
                    region.Width,
                    region.Height,
                    request.PixelFormat,
                    request.PixelType,
                    dataPtr);
                break;
        }
    }

    private readonly record struct PreparedUpload(
        TextureUploadRequest Request,
        int ByteCount,
        int RowLength,
        int ImageHeight);

    private interface IPboUploadBackend : IDisposable
    {
        TextureStreamingBackendKind Kind { get; }
        bool TryStageUpload(in PreparedUpload prepared, out PboUpload upload);
        void SubmitUpload(in PboUpload upload);
        void Tick();
    }

    private sealed class PersistentMappedPboRing : IPboUploadBackend
    {
        private readonly int bufferSizeBytes;
        private readonly int alignment;
        private readonly bool coherent;
        private readonly Queue<PendingRegion> inFlight = new();

        private int bufferId;
        private IntPtr mappedPtr;
        private int head;
        private int tail;

        public PersistentMappedPboRing(int bufferSizeBytes, int alignment, bool coherent)
        {
            if (bufferSizeBytes <= 0)
            {
                bufferSizeBytes = 1;
            }

            this.bufferSizeBytes = bufferSizeBytes;
            this.alignment = Math.Max(1, alignment);
            this.coherent = coherent;

            bufferId = GL.GenBuffer();
            GL.BindBuffer(BufferTarget.PixelUnpackBuffer, bufferId);

            BufferStorageFlags storageFlags = BufferStorageFlags.MapWriteBit | BufferStorageFlags.MapPersistentBit;
            if (coherent)
            {
                storageFlags |= BufferStorageFlags.MapCoherentBit;
            }

            GL.BufferStorage(BufferTarget.PixelUnpackBuffer, bufferSizeBytes, IntPtr.Zero, storageFlags);

            BufferAccessMask mapFlags = BufferAccessMask.MapWriteBit | BufferAccessMask.MapPersistentBit;
            if (coherent)
            {
                mapFlags |= BufferAccessMask.MapCoherentBit;
            }
            else
            {
                mapFlags |= BufferAccessMask.MapFlushExplicitBit;
            }

            mappedPtr = GL.MapBufferRange(BufferTarget.PixelUnpackBuffer, IntPtr.Zero, bufferSizeBytes, mapFlags);
            GL.BindBuffer(BufferTarget.PixelUnpackBuffer, 0);

            if (mappedPtr == IntPtr.Zero)
            {
                throw new InvalidOperationException("Failed to map persistent PBO.");
            }
        }
        public TextureStreamingBackendKind Kind => TextureStreamingBackendKind.PersistentMappedRing;

        public bool TryStageUpload(in PreparedUpload prepared, out PboUpload upload)
        {
            upload = default;

            ReleaseCompleted();

            int allocationSize = TextureStreamingUtils.AlignUp(prepared.ByteCount, alignment);
            if (!TryAllocate(allocationSize, out int offset))
            {
                return false;
            }

            unsafe
            {
                IntPtr ptr = IntPtr.Add(mappedPtr, offset);
                Span<byte> dst = new((void*)ptr, prepared.ByteCount);
                prepared.Request.Data.CopyTo(dst);
            }

            if (!coherent)
            {
                GL.BindBuffer(BufferTarget.PixelUnpackBuffer, bufferId);
                GL.FlushMappedBufferRange(BufferTarget.PixelUnpackBuffer, (IntPtr)offset, prepared.ByteCount);
                GL.BindBuffer(BufferTarget.PixelUnpackBuffer, 0);
            }

            upload = new PboUpload(bufferId, offset, allocationSize, -1);
            return true;
        }

        public void SubmitUpload(in PboUpload upload)
        {
            GpuFence fence = GpuFence.Insert();
            inFlight.Enqueue(new PendingRegion(upload.OffsetBytes, upload.AllocationSizeBytes, fence));
        }

        public void Tick()
        {
            ReleaseCompleted();
        }

        public void Dispose()
        {
            while (inFlight.Count > 0)
            {
                PendingRegion region = inFlight.Dequeue();
                region.Fence.Dispose();
            }

            if (bufferId != 0)
            {
                GL.BindBuffer(BufferTarget.PixelUnpackBuffer, bufferId);
                GL.UnmapBuffer(BufferTarget.PixelUnpackBuffer);
                GL.BindBuffer(BufferTarget.PixelUnpackBuffer, 0);
                GL.DeleteBuffer(bufferId);
                bufferId = 0;
            }
        }

        private void ReleaseCompleted()
        {
            while (inFlight.Count > 0)
            {
                PendingRegion region = inFlight.Peek();
                if (!region.Fence.TryConsumeIfSignaled())
                {
                    break;
                }

                inFlight.Dequeue();
                tail = (region.Offset + region.Size) % bufferSizeBytes;
            }
        }

        private bool TryAllocate(int size, out int offset)
        {
            offset = 0;

            int alignedHead = TextureStreamingUtils.AlignUp(head, alignment);
            if (head >= tail)
            {
                if (alignedHead + size <= bufferSizeBytes)
                {
                    offset = alignedHead;
                    head = alignedHead + size;
                    return true;
                }

                if (size <= tail)
                {
                    offset = 0;
                    head = size;
                    return true;
                }

                return false;
            }

            if (alignedHead + size <= tail)
            {
                offset = alignedHead;
                head = alignedHead + size;
                return true;
            }

            return false;
        }

        private readonly record struct PendingRegion(int Offset, int Size, GpuFence Fence);
    }

    private sealed class TripleBufferedPboPool : IPboUploadBackend
    {
        private const int BufferCount = 3;

        private readonly PboSlot[] slots = new PboSlot[BufferCount];
        private readonly int bufferSizeBytes;
        private int nextIndex;

        public TripleBufferedPboPool(int bufferSizeBytes)
        {
            if (bufferSizeBytes <= 0)
            {
                bufferSizeBytes = 1;
            }

            this.bufferSizeBytes = bufferSizeBytes;

            for (int i = 0; i < BufferCount; i++)
            {
                int id = GL.GenBuffer();
                GL.BindBuffer(BufferTarget.PixelUnpackBuffer, id);
                GL.BufferData(BufferTarget.PixelUnpackBuffer, bufferSizeBytes, IntPtr.Zero, BufferUsageHint.StreamDraw);
                GL.BindBuffer(BufferTarget.PixelUnpackBuffer, 0);
                slots[i] = new PboSlot(id);
            }
        }

        public TextureStreamingBackendKind Kind => TextureStreamingBackendKind.TripleBuffered;

        public bool TryStageUpload(in PreparedUpload prepared, out PboUpload upload)
        {
            upload = default;

            if (prepared.ByteCount > bufferSizeBytes)
            {
                return false;
            }

            if (!TryAcquireSlot(out int index))
            {
                return false;
            }

            int bufferId = slots[index].BufferId;

            GL.BindBuffer(BufferTarget.PixelUnpackBuffer, bufferId);
            GL.BufferData(BufferTarget.PixelUnpackBuffer, bufferSizeBytes, IntPtr.Zero, BufferUsageHint.StreamDraw);

            IntPtr ptr = GL.MapBufferRange(
                BufferTarget.PixelUnpackBuffer,
                IntPtr.Zero,
                prepared.ByteCount,
                BufferAccessMask.MapWriteBit | BufferAccessMask.MapInvalidateBufferBit);

            if (ptr == IntPtr.Zero)
            {
                GL.BindBuffer(BufferTarget.PixelUnpackBuffer, 0);
                return false;
            }

            unsafe
            {
                Span<byte> dst = new((void*)ptr, prepared.ByteCount);
                prepared.Request.Data.CopyTo(dst);
            }

            GL.UnmapBuffer(BufferTarget.PixelUnpackBuffer);
            GL.BindBuffer(BufferTarget.PixelUnpackBuffer, 0);

            upload = new PboUpload(bufferId, 0, prepared.ByteCount, index);
            return true;
        }

        public void SubmitUpload(in PboUpload upload)
        {
            int index = upload.SlotIndex;
            slots[index] = slots[index] with { Fence = GpuFence.Insert() };
            nextIndex = (index + 1) % BufferCount;
        }

        public void Tick()
        {
            ReleaseCompleted();
        }

        public void Dispose()
        {
            for (int i = 0; i < slots.Length; i++)
            {
                slots[i].Fence?.Dispose();

                if (slots[i].BufferId != 0)
                {
                    GL.DeleteBuffer(slots[i].BufferId);
                }
            }
        }

        private void ReleaseCompleted()
        {
            for (int i = 0; i < slots.Length; i++)
            {
                GpuFence? fence = slots[i].Fence;
                if (fence is null)
                {
                    continue;
                }

                if (!fence.TryConsumeIfSignaled())
                {
                    continue;
                }

                slots[i] = slots[i] with { Fence = null };
            }
        }

        private bool TryAcquireSlot(out int index)
        {
            for (int i = 0; i < slots.Length; i++)
            {
                int candidate = (nextIndex + i) % slots.Length;
                GpuFence? fence = slots[candidate].Fence;
                if (fence is null)
                {
                    index = candidate;
                    return true;
                }

                if (!fence.TryConsumeIfSignaled())
                {
                    continue;
                }

                slots[candidate] = slots[candidate] with { Fence = null };
                index = candidate;
                return true;
            }

            index = -1;
            return false;
        }

        private readonly record struct PboSlot(int BufferId)
        {
            public GpuFence? Fence { get; init; }
        }
    }
}
internal enum TextureStreamingBackendKind
{
    None = 0,
    PersistentMappedRing = 1,
    TripleBuffered = 2
}

internal readonly record struct TextureStreamingSettings(
    bool EnablePboStreaming,
    bool AllowDirectUploads,
    bool ForceDisablePersistent,
    bool UseCoherentMapping,
    int MaxUploadsPerFrame,
    int MaxBytesPerFrame,
    int MaxStagingBytes,
    int PersistentRingBytes,
    int TripleBufferBytes,
    int PboAlignment)
{
    public static TextureStreamingSettings Default { get; } = new(
        EnablePboStreaming: true,
        AllowDirectUploads: true,
        ForceDisablePersistent: false,
        UseCoherentMapping: true,
        MaxUploadsPerFrame: 8,
        MaxBytesPerFrame: 4 * 1024 * 1024,
        MaxStagingBytes: 8 * 1024 * 1024,
        PersistentRingBytes: 32 * 1024 * 1024,
        TripleBufferBytes: 8 * 1024 * 1024,
        PboAlignment: 256);
}

internal readonly record struct TextureStreamingDiagnostics(
    long Enqueued,
    long Uploaded,
    long UploadedBytes,
    long FallbackUploads,
    long DroppedInvalid,
    long Deferred,
    int Pending,
    TextureStreamingBackendKind Backend);

internal readonly record struct TextureUploadTarget(TextureTarget BindTarget, TextureTarget UploadTarget)
{
    public static TextureUploadTarget For1D() => new(TextureTarget.Texture1D, TextureTarget.Texture1D);
    public static TextureUploadTarget For1DArray() => new(TextureTarget.Texture1DArray, TextureTarget.Texture1DArray);
    public static TextureUploadTarget For2D() => new(TextureTarget.Texture2D, TextureTarget.Texture2D);
    public static TextureUploadTarget For2DArray() => new(TextureTarget.Texture2DArray, TextureTarget.Texture2DArray);
    public static TextureUploadTarget For3D() => new(TextureTarget.Texture3D, TextureTarget.Texture3D);
    public static TextureUploadTarget ForRectangle() => new(TextureTarget.TextureRectangle, TextureTarget.TextureRectangle);
    public static TextureUploadTarget ForCubeFace(TextureCubeFace face)
        => new(TextureTarget.TextureCubeMap, TextureStreamingUtils.GetCubeFaceTarget(face));
    public static TextureUploadTarget ForCubeArray() => new(TextureTarget.TextureCubeMapArray, TextureTarget.TextureCubeMapArray);
}

internal enum TextureCubeFace
{
    PositiveX = 0,
    NegativeX = 1,
    PositiveY = 2,
    NegativeY = 3,
    PositiveZ = 4,
    NegativeZ = 5
}

internal readonly record struct TextureUploadRegion(
    int X,
    int Y,
    int Z,
    int Width,
    int Height,
    int Depth,
    int MipLevel = 0)
{
    public static TextureUploadRegion For1D(int x, int width, int mipLevel = 0)
        => new(x, 0, 0, width, 1, 1, mipLevel);

    public static TextureUploadRegion For2D(int x, int y, int width, int height, int mipLevel = 0)
        => new(x, y, 0, width, height, 1, mipLevel);

    public static TextureUploadRegion For3D(int x, int y, int z, int width, int height, int depth, int mipLevel = 0)
        => new(x, y, z, width, height, depth, mipLevel);
}

internal readonly record struct TextureUploadRequest(
    int TextureId,
    TextureUploadTarget Target,
    TextureUploadRegion Region,
    PixelFormat PixelFormat,
    PixelType PixelType,
    TextureUploadData Data,
    int Priority = 0,
    int UnpackAlignment = 1,
    int UnpackRowLength = 0,
    int UnpackImageHeight = 0);

internal enum TextureUploadDataKind
{
    Bytes = 0,
    UShorts = 1,
    Halfs = 2,
    Floats = 3
}

internal readonly struct TextureUploadData
{
    public Array DataArray { get; }
    public TextureUploadDataKind Kind { get; }
    public int ByteLength { get; }

    public bool IsEmpty => DataArray is null || ByteLength == 0;

    private TextureUploadData(Array dataArray, TextureUploadDataKind kind, int byteLength)
    {
        DataArray = dataArray;
        Kind = kind;
        ByteLength = byteLength;
    }

    public static TextureUploadData From(byte[] data)
        => new(data ?? throw new ArgumentNullException(nameof(data)), TextureUploadDataKind.Bytes, data.Length);

    public static TextureUploadData From(ushort[] data)
        => new(data ?? throw new ArgumentNullException(nameof(data)), TextureUploadDataKind.UShorts, checked(data.Length * sizeof(ushort)));

    public static TextureUploadData From(Half[] data)
        => new(data ?? throw new ArgumentNullException(nameof(data)), TextureUploadDataKind.Halfs, checked(data.Length * sizeof(ushort)));

    public static TextureUploadData From(float[] data)
        => new(data ?? throw new ArgumentNullException(nameof(data)), TextureUploadDataKind.Floats, checked(data.Length * sizeof(float)));

    public void CopyTo(Span<byte> destination)
    {
        if (destination.Length > ByteLength)
        {
            throw new ArgumentException("Destination span exceeds available data.", nameof(destination));
        }

        switch (Kind)
        {
            case TextureUploadDataKind.Bytes:
                ((byte[])DataArray).AsSpan(0, destination.Length).CopyTo(destination);
                break;
            case TextureUploadDataKind.UShorts:
                MemoryMarshal.AsBytes(((ushort[])DataArray).AsSpan()).Slice(0, destination.Length).CopyTo(destination);
                break;
            case TextureUploadDataKind.Halfs:
                MemoryMarshal.AsBytes(((Half[])DataArray).AsSpan()).Slice(0, destination.Length).CopyTo(destination);
                break;
            case TextureUploadDataKind.Floats:
                MemoryMarshal.AsBytes(((float[])DataArray).AsSpan()).Slice(0, destination.Length).CopyTo(destination);
                break;
            default:
                throw new InvalidOperationException("Unsupported upload data kind.");
        }
    }
}

internal readonly record struct PboUpload(int BufferId, int OffsetBytes, int AllocationSizeBytes, int SlotIndex);

internal enum UploadDimension
{
    Tex1D = 0,
    Tex2D = 1,
    Tex3D = 2
}

internal static class TextureStreamingUtils
{
    public static bool SupportsBufferStorage()
    {
        string? extensions = GL.GetString(StringName.Extensions);
        return extensions is not null && extensions.Contains("GL_ARB_buffer_storage", StringComparison.Ordinal);
    }

    public static UploadDimension GetUploadDimension(TextureTarget target)
    {
        if (target == TextureTarget.Texture1D)
        {
            return UploadDimension.Tex1D;
        }

        if (Is3DTarget(target))
        {
            return UploadDimension.Tex3D;
        }

        return UploadDimension.Tex2D;
    }

    public static bool Is3DTarget(TextureTarget target)
    {
        return target == TextureTarget.Texture3D
            || target == TextureTarget.Texture2DArray
            || target == TextureTarget.TextureCubeMapArray;
    }

    public static TextureTarget GetCubeFaceTarget(TextureCubeFace face)
        => (TextureTarget)((int)TextureTarget.TextureCubeMapPositiveX + (int)face);

    public static int AlignUp(int value, int alignment)
    {
        if (alignment <= 1)
        {
            return value;
        }
        return ((value + alignment - 1) / alignment) * alignment;
    }

    public static int GetBytesPerPixel(PixelFormat format, PixelType type)
    {
        int channels = format switch
        {
            PixelFormat.Red => 1,
            PixelFormat.Rg => 2,
            PixelFormat.Rgb => 3,
            PixelFormat.Bgr => 3,
            PixelFormat.Rgba => 4,
            PixelFormat.Bgra => 4,
            PixelFormat.DepthComponent => 1,
            PixelFormat.DepthStencil => 1,
            _ => 4
        };

        int bytesPerChannel = type switch
        {
            PixelType.Byte => 1,
            PixelType.UnsignedByte => 1,
            PixelType.Short => 2,
            PixelType.UnsignedShort => 2,
            PixelType.HalfFloat => 2,
            PixelType.Int => 4,
            PixelType.UnsignedInt => 4,
            PixelType.Float => 4,
            PixelType.UnsignedInt248 => 4,
            PixelType.Float32UnsignedInt248Rev => 8,
            _ => 0
        };

        if (format == PixelFormat.DepthStencil)
        {
            return bytesPerChannel;
        }

        return channels * bytesPerChannel;
    }
}

internal sealed class PriorityFifoQueue<T>
{
    private readonly object gate = new();
    private readonly SortedDictionary<int, Queue<T>> queuesByPriority = new();

    public int Count
    {
        get
        {
            lock (gate)
            {
                int total = 0;
                foreach (Queue<T> q in queuesByPriority.Values)
                {
                    total += q.Count;
                }

                return total;
            }
        }
    }

    public void Enqueue(int priority, T item)
    {
        lock (gate)
        {
            if (!queuesByPriority.TryGetValue(priority, out Queue<T>? q))
            {
                q = new Queue<T>();
                queuesByPriority[priority] = q;
            }

            q.Enqueue(item);
        }
    }

    public bool TryDequeue(out T item)
    {
        lock (gate)
        {
            if (queuesByPriority.Count == 0)
            {
                item = default!;
                return false;
            }

            int highestPriority = int.MinValue;
            Queue<T>? highestQueue = null;

            foreach ((int priority, Queue<T> q) in queuesByPriority)
            {
                highestPriority = priority;
                highestQueue = q;
            }

            if (highestQueue is null || highestQueue.Count == 0)
            {
                item = default!;
                return false;
            }

            item = highestQueue.Dequeue();
            if (highestQueue.Count == 0)
            {
                queuesByPriority.Remove(highestPriority);
            }

            return true;
        }
    }
}
