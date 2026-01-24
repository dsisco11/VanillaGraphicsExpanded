using System;
using System.Buffers;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading;

using OpenTK.Graphics.OpenGL;

namespace VanillaGraphicsExpanded.Rendering;

internal sealed class TextureStreamingManager : IDisposable
{
    private const int DefaultQueueCapacity = 65_536;

    private readonly IUploadCommandQueue commandQueue;
    private readonly IUploadScheduler scheduler;
    private UploadCommand[] carryOver = Array.Empty<UploadCommand>();
    private int carryOverCount;

    private readonly ConcurrentQueue<UploadCommand> overflowFallbackQueue = new();
    private int overflowFallbackCount;

    private TripleBufferedPboPool? fallbackPboPool;

    private TextureStreamingSettings settings;
    private IPboUploadBackend? backend;
    private bool backendInitialized;
    private int backendResetRequested;
    private bool disposed;

    private long nextSequenceId;

    private readonly long[] priorityEnqueued = new long[3];
    private readonly long[] priorityDrained = new long[3];
    private readonly long[] priorityFallback = new long[3];
    private readonly long[] priorityFailed = new long[3];

    private long enqueued;
    private long uploaded;
    private long uploadedBytes;
    private long fallbackUploads;
    private long droppedInvalid;
    private long deferredCount;

    private long stageCopyStaged;

    private long fallbackQueueFull;
    private long fallbackRingFull;
    private long fallbackNotInitialized;
    private long fallbackNoPersistentSupport;
    private long fallbackOversize;
    private long fallbackDisabled;

    public TextureStreamingManager(TextureStreamingSettings? settings = null)
    {
        this.settings = settings ?? TextureStreamingSettings.Default;
        commandQueue = new SingleChannelUploadQueue(DefaultQueueCapacity);
        scheduler = new StablePriorityUploadScheduler();
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

        // If the active backend is the fallback PBO pool, resizing requires a reset.
        // Keep this separate from RequiresBackendReset() so we don't unnecessarily reset
        // the persistent ring backend when only the per-frame upload budget changes.
        if (backend is TripleBufferedPboPool activePool)
        {
            int desiredSlots = ComputePboSlotCount(value);

            if (activePool.SlotCount != desiredSlots || activePool.BufferSizeBytes != value.TripleBufferBytes)
            {
                RequestBackendReset();
            }
        }

        // StageCopy fallback pool should track settings immediately.
        if (fallbackPboPool is not null)
        {
            int desiredSlots = ComputePboSlotCount(value);
            if (fallbackPboPool.SlotCount != desiredSlots || fallbackPboPool.BufferSizeBytes != value.TripleBufferBytes)
            {
                fallbackPboPool.Dispose();
                fallbackPboPool = null;
            }
        }
    }

    public void RequestBackendReset()
    {
        Interlocked.Exchange(ref backendResetRequested, 1);
    }

    public void Enqueue(TextureUploadRequest request)
    {
        long sequenceId = Interlocked.Increment(ref nextSequenceId);
        UploadCommand cmd = UploadCommand.FromCpu(sequenceId, request.Priority, request);

        if (!commandQueue.TryEnqueue(cmd))
        {
            Interlocked.Increment(ref priorityFailed[GetPriorityIndex(request.Priority)]);
            return;
        }

        Interlocked.Increment(ref enqueued);
        Interlocked.Increment(ref priorityEnqueued[GetPriorityIndex(request.Priority)]);
    }

    public TextureStageResult StageCopy(
        int textureId,
        TextureUploadTarget target,
        TextureUploadRegion region,
        PixelFormat pixelFormat,
        PixelType pixelType,
        ReadOnlySpan<byte> data,
        TextureUploadPriority priority = TextureUploadPriority.Normal,
        int unpackAlignment = 1,
        int unpackRowLength = 0,
        int unpackImageHeight = 0)
    {
        if (disposed)
        {
            return TextureStageResult.Rejected(TextureStageRejectReason.ManagerDisposed);
        }

        if (textureId <= 0)
        {
            return TextureStageResult.Rejected(TextureStageRejectReason.InvalidArguments);
        }

        if (!TryComputeUploadByteCount(target.UploadTarget, region, pixelFormat, pixelType, unpackRowLength, unpackImageHeight, out int requiredBytes))
        {
            return TextureStageResult.Rejected(TextureStageRejectReason.InvalidArguments);
        }

        if (data.Length < requiredBytes)
        {
            return TextureStageResult.Rejected(TextureStageRejectReason.DataTooSmall);
        }

        if (!commandQueue.TryAcquireEnqueueSlot(out UploadEnqueueToken token))
        {
            TrackFallbackReason(TextureStageFallbackReason.QueueFull);
            return EnqueueOwnedFallbackToOverflow(
                textureId,
                target,
                region,
                pixelFormat,
                pixelType,
                data.Slice(0, requiredBytes),
                requiredBytes,
                (int)priority,
                unpackAlignment,
                unpackRowLength,
                unpackImageHeight);
        }

        TextureUploadRequest request = new(
            TextureId: textureId,
            Target: target,
            Region: region,
            PixelFormat: pixelFormat,
            PixelType: pixelType,
            Data: default,
            Priority: (int)priority,
            UnpackAlignment: unpackAlignment,
            UnpackRowLength: unpackRowLength,
            UnpackImageHeight: unpackImageHeight);

        int rowLength = unpackRowLength > 0 ? unpackRowLength : region.Width;
        int imageHeight = unpackImageHeight > 0 ? unpackImageHeight : region.Height;

        if (TryStageCopyToPersistentRing(
            data.Slice(0, requiredBytes),
            requiredBytes,
            out PboUpload pboUpload,
            out MappedFlushRange flushRange,
            out TextureStageFallbackReason fallbackReason))
        {
            long sequenceId = Interlocked.Increment(ref nextSequenceId);
            UploadCommand cmd = UploadCommand.FromPersistentRing(
                sequenceId,
                (int)priority,
                request,
                requiredBytes,
                rowLength,
                imageHeight,
                pboUpload,
                flushRange);

            if (!commandQueue.TryEnqueue(token, cmd))
            {
                commandQueue.ReleaseEnqueueSlot(token);
                Interlocked.Increment(ref priorityFailed[GetPriorityIndex((int)priority)]);
                return TextureStageResult.Rejected(TextureStageRejectReason.QueueFull);
            }

            Interlocked.Increment(ref enqueued);
            Interlocked.Increment(ref priorityEnqueued[GetPriorityIndex((int)priority)]);
            Interlocked.Increment(ref stageCopyStaged);
            return TextureStageResult.StagedToPersistentRing();
        }

        TrackFallbackReason(fallbackReason);

        OwnedCpuUploadBuffer owned = CopyToOwnedCpuBuffer(data.Slice(0, requiredBytes), requiredBytes);

        long fallbackSequenceId = Interlocked.Increment(ref nextSequenceId);
        UploadCommand fallbackCmd = UploadCommand.FromOwnedCpuBytes(
            fallbackSequenceId,
            (int)priority,
            request,
            requiredBytes,
            rowLength,
            imageHeight,
            owned);

        if (!commandQueue.TryEnqueue(token, fallbackCmd))
        {
            commandQueue.ReleaseEnqueueSlot(token);
            owned.Dispose();
            TrackFallbackReason(TextureStageFallbackReason.QueueFull);
            return EnqueueOwnedFallbackToOverflow(
                textureId,
                target,
                region,
                pixelFormat,
                pixelType,
                data.Slice(0, requiredBytes),
                requiredBytes,
                (int)priority,
                unpackAlignment,
                unpackRowLength,
                unpackImageHeight);
        }

        Interlocked.Increment(ref enqueued);
        Interlocked.Increment(ref priorityEnqueued[GetPriorityIndex((int)priority)]);
        return TextureStageResult.EnqueuedFallback();
    }

    public TextureStageResult StageCopy(
        int textureId,
        TextureUploadTarget target,
        TextureUploadRegion region,
        PixelFormat pixelFormat,
        PixelType pixelType,
        ReadOnlySpan<ushort> data,
        TextureUploadPriority priority = TextureUploadPriority.Normal,
        int unpackAlignment = 1,
        int unpackRowLength = 0,
        int unpackImageHeight = 0)
    {
        if (pixelType != PixelType.UnsignedShort && pixelType != PixelType.Short)
        {
            return TextureStageResult.Rejected(TextureStageRejectReason.DataTypeMismatch);
        }

        if (!TryComputeUploadByteCount(target.UploadTarget, region, pixelFormat, pixelType, unpackRowLength, unpackImageHeight, out int requiredBytes))
        {
            return TextureStageResult.Rejected(TextureStageRejectReason.InvalidArguments);
        }

        if ((requiredBytes % sizeof(ushort)) != 0)
        {
            return TextureStageResult.Rejected(TextureStageRejectReason.InvalidArguments);
        }

        int requiredElements = requiredBytes / sizeof(ushort);
        if (data.Length < requiredElements)
        {
            return TextureStageResult.Rejected(TextureStageRejectReason.DataTooSmall);
        }

        if (!commandQueue.TryAcquireEnqueueSlot(out UploadEnqueueToken token))
        {
            TrackFallbackReason(TextureStageFallbackReason.QueueFull);
            ReadOnlySpan<byte> bytes0 = MemoryMarshal.AsBytes(data.Slice(0, requiredElements));
            return EnqueueOwnedFallbackToOverflow(
                textureId,
                target,
                region,
                pixelFormat,
                pixelType,
                bytes0,
                requiredBytes,
                (int)priority,
                unpackAlignment,
                unpackRowLength,
                unpackImageHeight);
        }

        TextureUploadRequest request = new(
            TextureId: textureId,
            Target: target,
            Region: region,
            PixelFormat: pixelFormat,
            PixelType: pixelType,
            Data: default,
            Priority: (int)priority,
            UnpackAlignment: unpackAlignment,
            UnpackRowLength: unpackRowLength,
            UnpackImageHeight: unpackImageHeight);

        int rowLength = unpackRowLength > 0 ? unpackRowLength : region.Width;
        int imageHeight = unpackImageHeight > 0 ? unpackImageHeight : region.Height;

        ReadOnlySpan<byte> bytes = MemoryMarshal.AsBytes(data.Slice(0, requiredElements));
        if (TryStageCopyToPersistentRing(bytes, requiredBytes, out PboUpload pboUpload, out MappedFlushRange flushRange, out TextureStageFallbackReason fallbackReason))
        {
            long sequenceId = Interlocked.Increment(ref nextSequenceId);
            UploadCommand cmd = UploadCommand.FromPersistentRing(
                sequenceId,
                (int)priority,
                request,
                requiredBytes,
                rowLength,
                imageHeight,
                pboUpload,
                flushRange);

            if (!commandQueue.TryEnqueue(token, cmd))
            {
                commandQueue.ReleaseEnqueueSlot(token);
                Interlocked.Increment(ref priorityFailed[GetPriorityIndex((int)priority)]);
                return TextureStageResult.Rejected(TextureStageRejectReason.QueueFull);
            }

            Interlocked.Increment(ref enqueued);
            Interlocked.Increment(ref priorityEnqueued[GetPriorityIndex((int)priority)]);
            Interlocked.Increment(ref stageCopyStaged);
            return TextureStageResult.StagedToPersistentRing();
        }

        TrackFallbackReason(fallbackReason);

        OwnedCpuUploadBuffer owned = CopyToOwnedCpuBuffer(bytes, requiredBytes);

        long fallbackSequenceId = Interlocked.Increment(ref nextSequenceId);
        UploadCommand fallbackCmd = UploadCommand.FromOwnedCpuBytes(
            fallbackSequenceId,
            (int)priority,
            request,
            requiredBytes,
            rowLength,
            imageHeight,
            owned);

        if (!commandQueue.TryEnqueue(token, fallbackCmd))
        {
            commandQueue.ReleaseEnqueueSlot(token);
            owned.Dispose();
            TrackFallbackReason(TextureStageFallbackReason.QueueFull);
            return EnqueueOwnedFallbackToOverflow(
                textureId,
                target,
                region,
                pixelFormat,
                pixelType,
                bytes,
                requiredBytes,
                (int)priority,
                unpackAlignment,
                unpackRowLength,
                unpackImageHeight);
        }

        Interlocked.Increment(ref enqueued);
        Interlocked.Increment(ref priorityEnqueued[GetPriorityIndex((int)priority)]);
        return TextureStageResult.EnqueuedFallback();
    }

    public TextureStageResult StageCopy(
        int textureId,
        TextureUploadTarget target,
        TextureUploadRegion region,
        PixelFormat pixelFormat,
        PixelType pixelType,
        ReadOnlySpan<Half> data,
        TextureUploadPriority priority = TextureUploadPriority.Normal,
        int unpackAlignment = 1,
        int unpackRowLength = 0,
        int unpackImageHeight = 0)
    {
        if (pixelType != PixelType.HalfFloat)
        {
            return TextureStageResult.Rejected(TextureStageRejectReason.DataTypeMismatch);
        }

        if (!TryComputeUploadByteCount(target.UploadTarget, region, pixelFormat, pixelType, unpackRowLength, unpackImageHeight, out int requiredBytes))
        {
            return TextureStageResult.Rejected(TextureStageRejectReason.InvalidArguments);
        }

        if ((requiredBytes % sizeof(ushort)) != 0)
        {
            return TextureStageResult.Rejected(TextureStageRejectReason.InvalidArguments);
        }

        int requiredElements = requiredBytes / sizeof(ushort);
        if (data.Length < requiredElements)
        {
            return TextureStageResult.Rejected(TextureStageRejectReason.DataTooSmall);
        }

        if (!commandQueue.TryAcquireEnqueueSlot(out UploadEnqueueToken token))
        {
            TrackFallbackReason(TextureStageFallbackReason.QueueFull);
            ReadOnlySpan<byte> bytes0 = MemoryMarshal.AsBytes(data.Slice(0, requiredElements));
            return EnqueueOwnedFallbackToOverflow(
                textureId,
                target,
                region,
                pixelFormat,
                pixelType,
                bytes0,
                requiredBytes,
                (int)priority,
                unpackAlignment,
                unpackRowLength,
                unpackImageHeight);
        }

        TextureUploadRequest request = new(
            TextureId: textureId,
            Target: target,
            Region: region,
            PixelFormat: pixelFormat,
            PixelType: pixelType,
            Data: default,
            Priority: (int)priority,
            UnpackAlignment: unpackAlignment,
            UnpackRowLength: unpackRowLength,
            UnpackImageHeight: unpackImageHeight);

        int rowLength = unpackRowLength > 0 ? unpackRowLength : region.Width;
        int imageHeight = unpackImageHeight > 0 ? unpackImageHeight : region.Height;

        ReadOnlySpan<byte> bytes = MemoryMarshal.AsBytes(data.Slice(0, requiredElements));
        if (TryStageCopyToPersistentRing(bytes, requiredBytes, out PboUpload pboUpload, out MappedFlushRange flushRange, out TextureStageFallbackReason fallbackReason))
        {
            long sequenceId = Interlocked.Increment(ref nextSequenceId);
            UploadCommand cmd = UploadCommand.FromPersistentRing(
                sequenceId,
                (int)priority,
                request,
                requiredBytes,
                rowLength,
                imageHeight,
                pboUpload,
                flushRange);

            if (!commandQueue.TryEnqueue(token, cmd))
            {
                commandQueue.ReleaseEnqueueSlot(token);
                Interlocked.Increment(ref priorityFailed[GetPriorityIndex((int)priority)]);
                return TextureStageResult.Rejected(TextureStageRejectReason.QueueFull);
            }

            Interlocked.Increment(ref enqueued);
            Interlocked.Increment(ref priorityEnqueued[GetPriorityIndex((int)priority)]);
            Interlocked.Increment(ref stageCopyStaged);
            return TextureStageResult.StagedToPersistentRing();
        }

        TrackFallbackReason(fallbackReason);

        OwnedCpuUploadBuffer owned = CopyToOwnedCpuBuffer(bytes, requiredBytes);

        long fallbackSequenceId = Interlocked.Increment(ref nextSequenceId);
        UploadCommand fallbackCmd = UploadCommand.FromOwnedCpuBytes(
            fallbackSequenceId,
            (int)priority,
            request,
            requiredBytes,
            rowLength,
            imageHeight,
            owned);

        if (!commandQueue.TryEnqueue(token, fallbackCmd))
        {
            commandQueue.ReleaseEnqueueSlot(token);
            owned.Dispose();
            TrackFallbackReason(TextureStageFallbackReason.QueueFull);
            return EnqueueOwnedFallbackToOverflow(
                textureId,
                target,
                region,
                pixelFormat,
                pixelType,
                bytes,
                requiredBytes,
                (int)priority,
                unpackAlignment,
                unpackRowLength,
                unpackImageHeight);
        }

        Interlocked.Increment(ref enqueued);
        Interlocked.Increment(ref priorityEnqueued[GetPriorityIndex((int)priority)]);
        return TextureStageResult.EnqueuedFallback();
    }

    public TextureStageResult StageCopy(
        int textureId,
        TextureUploadTarget target,
        TextureUploadRegion region,
        PixelFormat pixelFormat,
        PixelType pixelType,
        ReadOnlySpan<float> data,
        TextureUploadPriority priority = TextureUploadPriority.Normal,
        int unpackAlignment = 1,
        int unpackRowLength = 0,
        int unpackImageHeight = 0)
    {
        if (pixelType != PixelType.Float)
        {
            return TextureStageResult.Rejected(TextureStageRejectReason.DataTypeMismatch);
        }

        if (!TryComputeUploadByteCount(target.UploadTarget, region, pixelFormat, pixelType, unpackRowLength, unpackImageHeight, out int requiredBytes))
        {
            return TextureStageResult.Rejected(TextureStageRejectReason.InvalidArguments);
        }

        if ((requiredBytes % sizeof(float)) != 0)
        {
            return TextureStageResult.Rejected(TextureStageRejectReason.InvalidArguments);
        }

        int requiredElements = requiredBytes / sizeof(float);
        if (data.Length < requiredElements)
        {
            return TextureStageResult.Rejected(TextureStageRejectReason.DataTooSmall);
        }

        if (!commandQueue.TryAcquireEnqueueSlot(out UploadEnqueueToken token))
        {
            TrackFallbackReason(TextureStageFallbackReason.QueueFull);
            ReadOnlySpan<byte> bytes0 = MemoryMarshal.AsBytes(data.Slice(0, requiredElements));
            return EnqueueOwnedFallbackToOverflow(
                textureId,
                target,
                region,
                pixelFormat,
                pixelType,
                bytes0,
                requiredBytes,
                (int)priority,
                unpackAlignment,
                unpackRowLength,
                unpackImageHeight);
        }

        TextureUploadRequest request = new(
            TextureId: textureId,
            Target: target,
            Region: region,
            PixelFormat: pixelFormat,
            PixelType: pixelType,
            Data: default,
            Priority: (int)priority,
            UnpackAlignment: unpackAlignment,
            UnpackRowLength: unpackRowLength,
            UnpackImageHeight: unpackImageHeight);

        int rowLength = unpackRowLength > 0 ? unpackRowLength : region.Width;
        int imageHeight = unpackImageHeight > 0 ? unpackImageHeight : region.Height;

        ReadOnlySpan<byte> bytes = MemoryMarshal.AsBytes(data.Slice(0, requiredElements));
        if (TryStageCopyToPersistentRing(bytes, requiredBytes, out PboUpload pboUpload, out MappedFlushRange flushRange, out TextureStageFallbackReason fallbackReason))
        {
            long sequenceId = Interlocked.Increment(ref nextSequenceId);
            UploadCommand cmd = UploadCommand.FromPersistentRing(
                sequenceId,
                (int)priority,
                request,
                requiredBytes,
                rowLength,
                imageHeight,
                pboUpload,
                flushRange);

            if (!commandQueue.TryEnqueue(token, cmd))
            {
                commandQueue.ReleaseEnqueueSlot(token);
                Interlocked.Increment(ref priorityFailed[GetPriorityIndex((int)priority)]);
                return TextureStageResult.Rejected(TextureStageRejectReason.QueueFull);
            }

            Interlocked.Increment(ref enqueued);
            Interlocked.Increment(ref priorityEnqueued[GetPriorityIndex((int)priority)]);
            Interlocked.Increment(ref stageCopyStaged);
            return TextureStageResult.StagedToPersistentRing();
        }

        TrackFallbackReason(fallbackReason);

        OwnedCpuUploadBuffer owned = CopyToOwnedCpuBuffer(bytes, requiredBytes);

        long fallbackSequenceId = Interlocked.Increment(ref nextSequenceId);
        UploadCommand fallbackCmd = UploadCommand.FromOwnedCpuBytes(
            fallbackSequenceId,
            (int)priority,
            request,
            requiredBytes,
            rowLength,
            imageHeight,
            owned);

        if (!commandQueue.TryEnqueue(token, fallbackCmd))
        {
            commandQueue.ReleaseEnqueueSlot(token);
            owned.Dispose();
            TrackFallbackReason(TextureStageFallbackReason.QueueFull);
            return EnqueueOwnedFallbackToOverflow(
                textureId,
                target,
                region,
                pixelFormat,
                pixelType,
                bytes,
                requiredBytes,
                (int)priority,
                unpackAlignment,
                unpackRowLength,
                unpackImageHeight);
        }

        Interlocked.Increment(ref enqueued);
        Interlocked.Increment(ref priorityEnqueued[GetPriorityIndex((int)priority)]);
        return TextureStageResult.EnqueuedFallback();
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

        int pendingCount = commandQueue.Count + Volatile.Read(ref carryOverCount) + Volatile.Read(ref overflowFallbackCount);
        if (pendingCount == 0)
        {
            return;
        }

        EnsureBackend();

        int maxBatchCommands = ComputeMaxDrainBatchSize(settings);
        int targetBatchSize = Math.Min(pendingCount, maxBatchCommands);
        UploadCommand[] work = ArrayPool<UploadCommand>.Shared.Rent(Math.Max(64, targetBatchSize));
        int workCount = 0;

        try
        {
            if (carryOverCount > 0)
            {
                int take = Math.Min(carryOverCount, work.Length);
                Array.Copy(carryOver, 0, work, 0, take);
                workCount = take;

                if (take < carryOverCount)
                {
                    int remaining = carryOverCount - take;
                    Array.Copy(carryOver, take, carryOver, 0, remaining);
                    carryOverCount = remaining;
                }
                else
                {
                    carryOverCount = 0;
                }
            }

            while (workCount < work.Length)
            {
                int drained = commandQueue.Drain(work.AsSpan(workCount));
                if (drained == 0)
                {
                    break;
                }

                AccumulatePriorityCounts(work.AsSpan(workCount, drained), priorityDrained);
                workCount += drained;
            }

            while (workCount < work.Length && overflowFallbackQueue.TryDequeue(out UploadCommand overflowCmd))
            {
                work[workCount++] = overflowCmd;
                Interlocked.Decrement(ref overflowFallbackCount);
                Interlocked.Increment(ref priorityDrained[GetPriorityIndex(overflowCmd.Priority)]);
            }

            scheduler.Sort(work, workCount);

            int uploads = 0;
            int bytes = 0;

            for (int i = 0; i < workCount; i++)
            {
                if (uploads >= settings.MaxUploadsPerFrame || bytes >= settings.MaxBytesPerFrame)
                {
                    CarryOver(work, i, workCount - i);
                    return;
                }

                UploadCommand cmd = work[i];
                TextureUploadRequest request = cmd.Request;

                PreparedUpload prepared;
                if (cmd.Kind == UploadCommandKind.FromPersistentRing)
                {
                    if (cmd.ByteCount <= 0 || cmd.RowLength <= 0 || cmd.ImageHeight <= 0)
                    {
                        Interlocked.Increment(ref droppedInvalid);
                        continue;
                    }

                    prepared = new PreparedUpload(request, cmd.ByteCount, cmd.RowLength, cmd.ImageHeight);
                }
                else if (cmd.Kind == UploadCommandKind.FromOwnedCpuBytes)
                {
                    if (cmd.OwnedBuffer is null || cmd.ByteCount <= 0 || cmd.RowLength <= 0 || cmd.ImageHeight <= 0)
                    {
                        cmd.OwnedBuffer?.Dispose();
                        Interlocked.Increment(ref droppedInvalid);
                        continue;
                    }

                    prepared = new PreparedUpload(request, cmd.ByteCount, cmd.RowLength, cmd.ImageHeight);
                }
                else
                {
                    if (cmd.ByteCount > 0)
                    {
                        prepared = new PreparedUpload(request, cmd.ByteCount, cmd.RowLength, cmd.ImageHeight);
                    }
                    else if (!TryPrepareRequest(request, out prepared))
                    {
                        Interlocked.Increment(ref droppedInvalid);
                        continue;
                    }
                }

                int byteCount = prepared.ByteCount;
                bool uploadedThisFrame = false;

                if (cmd.Kind == UploadCommandKind.FromPersistentRing)
                {
                    if (backend is not PersistentMappedPboRing ring || cmd.PboUpload.BufferId != ring.BufferId)
                    {
                        Interlocked.Increment(ref droppedInvalid);
                        continue;
                    }

                    if (!cmd.FlushRange.IsEmpty)
                    {
                        ring.FlushMappedRange(cmd.FlushRange);
                    }

                    IssuePboUpload(prepared, cmd.PboUpload);
                    ring.SubmitUpload(cmd.PboUpload);
                    uploadedThisFrame = true;
                }

                if (!uploadedThisFrame && cmd.Kind == UploadCommandKind.FromOwnedCpuBytes)
                {
                    OwnedCpuUploadBuffer owned = cmd.OwnedBuffer!;

                    if (TryStageOwnedFallbackToTripleBuffer(owned, prepared, out PboUpload upload))
                    {
                        IssuePboUpload(prepared, upload);
                        EnsureFallbackPboPool().SubmitUpload(upload);
                        uploadedThisFrame = true;
                    }
                    else if (settings.AllowDirectUploads && TryUploadDirectFromOwnedBytes(owned, prepared))
                    {
                        Interlocked.Increment(ref fallbackUploads);
                        Interlocked.Increment(ref priorityFallback[GetPriorityIndex(request.Priority)]);
                        uploadedThisFrame = true;
                    }

                    if (uploadedThisFrame)
                    {
                        owned.Dispose();
                    }
                    else
                    {
                        CarryOver(work, i, workCount - i);
                        Interlocked.Increment(ref deferredCount);
                        return;
                    }
                }

                if (!uploadedThisFrame && backend is not null && byteCount <= settings.MaxStagingBytes)
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
                        Interlocked.Increment(ref priorityFallback[GetPriorityIndex(request.Priority)]);
                        uploadedThisFrame = true;
                    }
                }

                if (!uploadedThisFrame)
                {
                    CarryOver(work, i, workCount - i);
                    Interlocked.Increment(ref deferredCount);
                    return;
                }

                uploads++;
                bytes += byteCount;
                Interlocked.Increment(ref uploaded);
                Interlocked.Add(ref uploadedBytes, byteCount);
            }
        }
        finally
        {
            ArrayPool<UploadCommand>.Shared.Return(work, clearArray: false);
        }
    }

    private static int ComputeMaxDrainBatchSize(in TextureStreamingSettings settings)
    {
        int maxByUploads = Math.Max(settings.MaxUploadsPerFrame, 1) * 4;
        int max = Math.Clamp(maxByUploads, 64, 4096);
        return max;
    }

    public TextureStreamingDiagnostics GetDiagnosticsSnapshot()
    {
        TextureStreamingPriorityDiagnostics priority = new(
            Low: new TextureStreamingPriorityCounter(
                Enqueued: Interlocked.Read(ref priorityEnqueued[0]),
                Drained: Interlocked.Read(ref priorityDrained[0]),
                Fallback: Interlocked.Read(ref priorityFallback[0]),
                Failed: Interlocked.Read(ref priorityFailed[0])),
            Normal: new TextureStreamingPriorityCounter(
                Enqueued: Interlocked.Read(ref priorityEnqueued[1]),
                Drained: Interlocked.Read(ref priorityDrained[1]),
                Fallback: Interlocked.Read(ref priorityFallback[1]),
                Failed: Interlocked.Read(ref priorityFailed[1])),
            High: new TextureStreamingPriorityCounter(
                Enqueued: Interlocked.Read(ref priorityEnqueued[2]),
                Drained: Interlocked.Read(ref priorityDrained[2]),
                Fallback: Interlocked.Read(ref priorityFallback[2]),
                Failed: Interlocked.Read(ref priorityFailed[2])));

        TextureStreamingFallbackDiagnostics fallback = new(
            QueueFull: Interlocked.Read(ref fallbackQueueFull),
            RingFull: Interlocked.Read(ref fallbackRingFull),
            NotInitialized: Interlocked.Read(ref fallbackNotInitialized),
            NoPersistentSupport: Interlocked.Read(ref fallbackNoPersistentSupport),
            Oversize: Interlocked.Read(ref fallbackOversize),
            Disabled: Interlocked.Read(ref fallbackDisabled));

        return new TextureStreamingDiagnostics(
            Enqueued: Interlocked.Read(ref enqueued),
            Uploaded: Interlocked.Read(ref uploaded),
            UploadedBytes: Interlocked.Read(ref uploadedBytes),
            FallbackUploads: Interlocked.Read(ref fallbackUploads),
            DroppedInvalid: Interlocked.Read(ref droppedInvalid),
            Deferred: Interlocked.Read(ref deferredCount),
            Pending: commandQueue.Count + Volatile.Read(ref carryOverCount) + Volatile.Read(ref overflowFallbackCount),
            Backend: backend?.Kind ?? TextureStreamingBackendKind.None,
            Priority: priority,
            StageCopyStaged: Interlocked.Read(ref stageCopyStaged),
            Fallback: fallback);
    }

    private bool TryStageCopyToPersistentRing(
        ReadOnlySpan<byte> bytes,
        int byteCount,
        out PboUpload upload,
        out MappedFlushRange flushRange,
        out TextureStageFallbackReason fallbackReason)
    {
        upload = default;
        flushRange = MappedFlushRange.None;
        fallbackReason = TextureStageFallbackReason.None;

        if (byteCount <= 0)
        {
            fallbackReason = TextureStageFallbackReason.Oversize;
            return false;
        }

        if (!backendInitialized)
        {
            fallbackReason = TextureStageFallbackReason.NotInitialized;
            return false;
        }

        if (byteCount > settings.MaxStagingBytes)
        {
            fallbackReason = TextureStageFallbackReason.Oversize;
            return false;
        }

        if (!settings.EnablePboStreaming)
        {
            fallbackReason = TextureStageFallbackReason.Disabled;
            return false;
        }

        if (backend is not PersistentMappedPboRing ring)
        {
            fallbackReason = TextureStageFallbackReason.NoPersistentSupport;
            return false;
        }

        if (!ring.TryStageCopy(bytes, byteCount, out upload, out flushRange))
        {
            fallbackReason = TextureStageFallbackReason.RingFull;
            return false;
        }

        return true;
    }

    private static OwnedCpuUploadBuffer CopyToOwnedCpuBuffer(ReadOnlySpan<byte> bytes, int byteCount)
    {
        IMemoryOwner<byte> owner = MemoryPool<byte>.Shared.Rent(byteCount);
        bytes.Slice(0, byteCount).CopyTo(owner.Memory.Span.Slice(0, byteCount));
        return new OwnedCpuUploadBuffer(owner, byteCount);
    }

    private TextureStageResult EnqueueOwnedFallbackToOverflow(
        int textureId,
        TextureUploadTarget target,
        TextureUploadRegion region,
        PixelFormat pixelFormat,
        PixelType pixelType,
        ReadOnlySpan<byte> bytes,
        int byteCount,
        int priority,
        int unpackAlignment,
        int unpackRowLength,
        int unpackImageHeight)
    {
        int rowLength = unpackRowLength > 0 ? unpackRowLength : region.Width;
        int imageHeight = unpackImageHeight > 0 ? unpackImageHeight : region.Height;

        TextureUploadRequest request = new(
            TextureId: textureId,
            Target: target,
            Region: region,
            PixelFormat: pixelFormat,
            PixelType: pixelType,
            Data: default,
            Priority: priority,
            UnpackAlignment: unpackAlignment,
            UnpackRowLength: unpackRowLength,
            UnpackImageHeight: unpackImageHeight);

        OwnedCpuUploadBuffer owned = CopyToOwnedCpuBuffer(bytes, byteCount);
        long sequenceId = Interlocked.Increment(ref nextSequenceId);
        UploadCommand cmd = UploadCommand.FromOwnedCpuBytes(sequenceId, priority, request, byteCount, rowLength, imageHeight, owned);

        overflowFallbackQueue.Enqueue(cmd);
        Interlocked.Increment(ref overflowFallbackCount);

        Interlocked.Increment(ref enqueued);
        Interlocked.Increment(ref priorityEnqueued[GetPriorityIndex(priority)]);

        return TextureStageResult.EnqueuedFallback();
    }

    private void TrackFallbackReason(TextureStageFallbackReason reason)
    {
        switch (reason)
        {
            case TextureStageFallbackReason.QueueFull:
                Interlocked.Increment(ref fallbackQueueFull);
                break;
            case TextureStageFallbackReason.RingFull:
                Interlocked.Increment(ref fallbackRingFull);
                break;
            case TextureStageFallbackReason.NotInitialized:
                Interlocked.Increment(ref fallbackNotInitialized);
                break;
            case TextureStageFallbackReason.NoPersistentSupport:
                Interlocked.Increment(ref fallbackNoPersistentSupport);
                break;
            case TextureStageFallbackReason.Oversize:
                Interlocked.Increment(ref fallbackOversize);
                break;
            case TextureStageFallbackReason.Disabled:
                Interlocked.Increment(ref fallbackDisabled);
                break;
        }
    }

    private TripleBufferedPboPool EnsureFallbackPboPool()
    {
        int desiredSlots = ComputePboSlotCount(settings);

        if (fallbackPboPool is null
            || fallbackPboPool.SlotCount != desiredSlots
            || fallbackPboPool.BufferSizeBytes != settings.TripleBufferBytes)
        {
            fallbackPboPool?.Dispose();
            fallbackPboPool = new TripleBufferedPboPool(settings.TripleBufferBytes, desiredSlots);
        }

        return fallbackPboPool;
    }

    private static int ComputePboSlotCount(in TextureStreamingSettings settings)
    {
        // The fallback backend uses one PBO per in-flight upload.
        // Match capacity to the per-frame upload cap so we can stage up to that many uploads
        // without immediately falling back to direct uploads.
        // Keep a small minimum to preserve the original "triple buffering" behavior.
        int maxUploads = settings.MaxUploadsPerFrame;
        int desired = Math.Max(3, maxUploads);
        return Math.Clamp(desired, 3, 512);
    }

    private bool TryStageOwnedFallbackToTripleBuffer(OwnedCpuUploadBuffer owned, in PreparedUpload prepared, out PboUpload upload)
    {
        upload = default;

        if (!settings.EnablePboStreaming)
        {
            return false;
        }

        if (prepared.ByteCount > settings.MaxStagingBytes)
        {
            return false;
        }

        ReadOnlySpan<byte> bytes = owned.Memory.Span;
        return EnsureFallbackPboPool().TryStageBytes(bytes, prepared.ByteCount, out upload);
    }

    private static unsafe bool TryUploadDirectFromOwnedBytes(OwnedCpuUploadBuffer owned, in PreparedUpload prepared)
    {
        if (owned.ByteCount < prepared.ByteCount)
        {
            return false;
        }

        ApplyPixelStore(prepared);

        try
        {
            TextureUploadRequest request = prepared.Request;
            GL.BindTexture(request.Target.BindTarget, request.TextureId);

            using System.Buffers.MemoryHandle handle = owned.Memory.Pin();
            UploadSubImage(prepared, (IntPtr)handle.Pointer);
            return true;
        }
        catch
        {
            return false;
        }
        finally
        {
            GL.BindTexture(prepared.Request.Target.BindTarget, 0);
            ResetPixelStore(prepared);
        }
    }

    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        backend?.Dispose();
        backend = null;

        fallbackPboPool?.Dispose();
        fallbackPboPool = null;

        while (overflowFallbackQueue.TryDequeue(out UploadCommand cmd))
        {
            cmd.OwnedBuffer?.Dispose();
        }

        DrainAndDisposeCommandQueue();

        if (carryOverCount > 0)
        {
            for (int i = 0; i < carryOverCount; i++)
            {
                carryOver[i].OwnedBuffer?.Dispose();
            }
        }

        if (carryOver.Length != 0)
        {
            ArrayPool<UploadCommand>.Shared.Return(carryOver, clearArray: false);
            carryOver = Array.Empty<UploadCommand>();
            carryOverCount = 0;
        }

        disposed = true;
    }

    private void DrainAndDisposeCommandQueue()
    {
        UploadCommand[] tmp = ArrayPool<UploadCommand>.Shared.Rent(256);
        try
        {
            while (true)
            {
                int n = commandQueue.Drain(tmp);
                if (n == 0)
                {
                    break;
                }

                for (int i = 0; i < n; i++)
                {
                    tmp[i].OwnedBuffer?.Dispose();
                }
            }
        }
        finally
        {
            ArrayPool<UploadCommand>.Shared.Return(tmp, clearArray: false);
        }
    }

    private static int GetPriorityIndex(int priority)
    {
        if (priority < 0)
        {
            return 0;
        }

        if (priority > 0)
        {
            return 2;
        }

        return 1;
    }

    private static void AccumulatePriorityCounts(ReadOnlySpan<UploadCommand> commands, long[] counters)
    {
        long low = 0;
        long normal = 0;
        long high = 0;

        for (int i = 0; i < commands.Length; i++)
        {
            int idx = GetPriorityIndex(commands[i].Priority);
            if (idx == 0)
            {
                low++;
            }
            else if (idx == 2)
            {
                high++;
            }
            else
            {
                normal++;
            }
        }

        if (low != 0)
        {
            Interlocked.Add(ref counters[0], low);
        }
        if (normal != 0)
        {
            Interlocked.Add(ref counters[1], normal);
        }
        if (high != 0)
        {
            Interlocked.Add(ref counters[2], high);
        }
    }

    private void EnsureCarryOverCapacity(int additional)
    {
        int required = carryOverCount + additional;
        if (carryOver.Length >= required)
        {
            return;
        }

        int newSize = Math.Max(64, carryOver.Length);
        while (newSize < required)
        {
            newSize *= 2;
        }

        UploadCommand[] grown = ArrayPool<UploadCommand>.Shared.Rent(newSize);
        if (carryOverCount > 0)
        {
            Array.Copy(carryOver, 0, grown, 0, carryOverCount);
        }

        if (carryOver.Length != 0)
        {
            ArrayPool<UploadCommand>.Shared.Return(carryOver, clearArray: false);
        }

        carryOver = grown;
    }

    private void CarryOver(UploadCommand[] source, int offset, int length)
    {
        if (length <= 0)
        {
            return;
        }

        EnsureCarryOverCapacity(length);
        Array.Copy(source, offset, carryOver, carryOverCount, length);
        carryOverCount += length;
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

        backend = new TripleBufferedPboPool(settings.TripleBufferBytes, ComputePboSlotCount(settings));
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

    private static bool TryComputeUploadByteCount(
        TextureTarget uploadTarget,
        in TextureUploadRegion region,
        PixelFormat pixelFormat,
        PixelType pixelType,
        int unpackRowLength,
        int unpackImageHeight,
        out int byteCount)
    {
        byteCount = 0;

        if (region.Width <= 0 || region.Height <= 0 || region.Depth <= 0)
        {
            return false;
        }

        UploadDimension dim = TextureStreamingUtils.GetUploadDimension(uploadTarget);

        if (dim == UploadDimension.Tex1D)
        {
            if (region.Height != 1 || region.Depth != 1)
            {
                return false;
            }
        }
        else if (dim == UploadDimension.Tex2D)
        {
            if (region.Depth != 1)
            {
                return false;
            }
        }

        int rowLength = unpackRowLength > 0 ? unpackRowLength : region.Width;
        int imageHeight = dim == UploadDimension.Tex1D
            ? 1
            : unpackImageHeight > 0 ? unpackImageHeight : region.Height;

        if (rowLength <= 0 || imageHeight <= 0)
        {
            return false;
        }

        if (rowLength < region.Width || imageHeight < region.Height)
        {
            return false;
        }

        int bytesPerPixel = TextureStreamingUtils.GetBytesPerPixel(pixelFormat, pixelType);
        if (bytesPerPixel <= 0)
        {
            return false;
        }

        long slices = dim == UploadDimension.Tex3D ? region.Depth : 1;
        long byteCountLong = (long)rowLength * imageHeight * slices * bytesPerPixel;
        if (byteCountLong <= 0 || byteCountLong > int.MaxValue)
        {
            return false;
        }

        byteCount = (int)byteCountLong;
        return true;
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

        UploadDimension dim = TextureStreamingUtils.GetUploadDimension(request.Target.UploadTarget);
        if (dim == UploadDimension.Tex1D)
        {
            if (region.Height != 1 || region.Depth != 1)
            {
                return false;
            }
        }
        else if (dim == UploadDimension.Tex2D)
        {
            if (region.Depth != 1)
            {
                return false;
            }
        }

        int rowLength = request.UnpackRowLength > 0 ? request.UnpackRowLength : region.Width;
        int imageHeight = dim == UploadDimension.Tex1D
            ? 1
            : request.UnpackImageHeight > 0 ? request.UnpackImageHeight : region.Height;
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

        long slices = dim == UploadDimension.Tex3D ? region.Depth : 1;
        long byteCountLong = (long)rowLength * imageHeight * slices * bytesPerPixel;
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
        private readonly object gate = new();
        private readonly int bufferSizeBytes;
        private readonly int alignment;
        private readonly bool coherent;
        private readonly Queue<PendingRegion> inFlight = new();

        private GpuPixelUnpackBuffer? buffer;
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

            buffer = GpuPixelUnpackBuffer.Create(debugName: "VGE_TextureStreaming_PersistentPboRing");
            mappedPtr = buffer.AllocateAndMapPersistent(bufferSizeBytes, coherent);

            if (mappedPtr == IntPtr.Zero)
            {
                buffer.Dispose();
                buffer = null;
                throw new InvalidOperationException("Failed to map persistent PBO.");
            }
        }
        public TextureStreamingBackendKind Kind => TextureStreamingBackendKind.PersistentMappedRing;

        public int BufferId => buffer?.BufferId ?? 0;

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
                buffer!.FlushMappedRange(offset, prepared.ByteCount);
            }

            upload = new PboUpload(BufferId, offset, allocationSize, -1);
            return true;
        }

        public bool TryStageCopy(ReadOnlySpan<byte> bytes, int byteCount, out PboUpload upload, out MappedFlushRange flushRange)
        {
            upload = default;
            flushRange = MappedFlushRange.None;

            if (byteCount <= 0 || bytes.Length < byteCount)
            {
                return false;
            }

            int allocationSize = TextureStreamingUtils.AlignUp(byteCount, alignment);
            if (allocationSize > bufferSizeBytes)
            {
                return false;
            }

            int offset;
            lock (gate)
            {
                if (!TryAllocateNoLock(allocationSize, out offset))
                {
                    return false;
                }
            }

            unsafe
            {
                IntPtr ptr = IntPtr.Add(mappedPtr, offset);
                Span<byte> dst = new((void*)ptr, byteCount);
                bytes.Slice(0, byteCount).CopyTo(dst);
            }

            if (!coherent)
            {
                flushRange = new MappedFlushRange(offset, byteCount);
            }

            upload = new PboUpload(BufferId, offset, allocationSize, -1);
            return true;
        }

        public void FlushMappedRange(in MappedFlushRange range)
        {
            if (coherent || range.IsEmpty)
            {
                return;
            }

            buffer!.FlushMappedRange(range.OffsetBytes, range.SizeBytes);
        }

        public void SubmitUpload(in PboUpload upload)
        {
            GpuFence fence = GpuFence.Insert();
            lock (gate)
            {
                inFlight.Enqueue(new PendingRegion(upload.OffsetBytes, upload.AllocationSizeBytes, fence));
            }
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

            if (buffer is not null && buffer.IsValid && mappedPtr != IntPtr.Zero)
            {
                _ = buffer.Unmap();
            }

            mappedPtr = IntPtr.Zero;
            buffer?.Dispose();
            buffer = null;
        }

        private void ReleaseCompleted()
        {
            while (true)
            {
                PendingRegion region;

                lock (gate)
                {
                    if (inFlight.Count == 0)
                    {
                        return;
                    }

                    region = inFlight.Peek();
                }

                if (!region.Fence.TryConsumeIfSignaled())
                {
                    return;
                }

                lock (gate)
                {
                    if (inFlight.Count == 0)
                    {
                        return;
                    }

                    PendingRegion dequeued = inFlight.Peek();
                    if (!ReferenceEquals(region.Fence, dequeued.Fence))
                    {
                        continue;
                    }

                    inFlight.Dequeue();
                    tail = (dequeued.Offset + dequeued.Size) % bufferSizeBytes;
                }
            }
        }

        private bool TryAllocate(int size, out int offset)
        {
            lock (gate)
            {
                return TryAllocateNoLock(size, out offset);
            }
        }

        private bool TryAllocateNoLock(int size, out int offset)
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
        private readonly PboSlot[] slots;
        private readonly int bufferSizeBytes;
        private int nextIndex;

        public TripleBufferedPboPool(int bufferSizeBytes, int slotCount)
        {
            if (bufferSizeBytes <= 0)
            {
                bufferSizeBytes = 1;
            }

            slotCount = Math.Clamp(slotCount, 1, 8192);

            this.bufferSizeBytes = bufferSizeBytes;

            slots = new PboSlot[slotCount];

            for (int i = 0; i < slots.Length; i++)
            {
                var buffer = GpuPixelUnpackBuffer.Create(BufferUsageHint.StreamDraw, debugName: $"VGE_TextureStreaming_TriplePbo_{i}");
                buffer.AllocateOrphan(bufferSizeBytes);

                slots[i] = new PboSlot(buffer);
            }
        }

        public int BufferSizeBytes => bufferSizeBytes;

        public int SlotCount => slots.Length;

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

            GpuPixelUnpackBuffer? buffer = slots[index].Buffer;
            if (buffer is null || !buffer.IsValid)
            {
                return false;
            }

            using var scope = buffer.BindScope();
            buffer.AllocateOrphan(bufferSizeBytes);

            IntPtr ptr = buffer.MapRange(
                offsetBytes: 0,
                byteCount: prepared.ByteCount,
                access: MapBufferAccessMask.MapWriteBit | MapBufferAccessMask.MapInvalidateBufferBit);

            if (ptr == IntPtr.Zero)
            {
                return false;
            }

            unsafe
            {
                Span<byte> dst = new((void*)ptr, prepared.ByteCount);
                prepared.Request.Data.CopyTo(dst);
            }

            _ = buffer.Unmap();

            upload = new PboUpload(buffer.BufferId, 0, prepared.ByteCount, index);
            return true;
        }

        public bool TryStageBytes(ReadOnlySpan<byte> bytes, int byteCount, out PboUpload upload)
        {
            upload = default;

            if (byteCount <= 0 || bytes.Length < byteCount)
            {
                return false;
            }

            if (byteCount > bufferSizeBytes)
            {
                return false;
            }

            if (!TryAcquireSlot(out int index))
            {
                return false;
            }

            GpuPixelUnpackBuffer? buffer = slots[index].Buffer;
            if (buffer is null || !buffer.IsValid)
            {
                return false;
            }

            using var scope = buffer.BindScope();
            buffer.AllocateOrphan(bufferSizeBytes);

            IntPtr ptr = buffer.MapRange(
                offsetBytes: 0,
                byteCount: byteCount,
                access: MapBufferAccessMask.MapWriteBit | MapBufferAccessMask.MapInvalidateBufferBit);

            if (ptr == IntPtr.Zero)
            {
                return false;
            }

            unsafe
            {
                Span<byte> dst = new((void*)ptr, byteCount);
                bytes.Slice(0, byteCount).CopyTo(dst);
            }

            _ = buffer.Unmap();

            upload = new PboUpload(buffer.BufferId, 0, byteCount, index);
            return true;
        }

        public void SubmitUpload(in PboUpload upload)
        {
            int index = upload.SlotIndex;
            slots[index] = slots[index] with { Fence = GpuFence.Insert() };
            nextIndex = (index + 1) % slots.Length;
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

                slots[i].Buffer?.Dispose();
                slots[i] = slots[i] with { Buffer = null };
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

        private readonly record struct PboSlot(GpuPixelUnpackBuffer? Buffer)
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

internal enum TextureUploadPriority
{
    Low = -100,
    Normal = 0,
    High = 100
}

internal enum TextureStageOutcome
{
    StagedToPersistentRing = 0,
    EnqueuedFallback = 1,
    Rejected = 2
}

internal enum TextureStageRejectReason
{
    None = 0,
    ManagerDisposed = 1,
    InvalidArguments = 2,
    DataTooSmall = 3,
    DataTypeMismatch = 4,
    QueueFull = 5
}

internal readonly record struct TextureStageResult(TextureStageOutcome Outcome, TextureStageRejectReason RejectReason = TextureStageRejectReason.None)
{
    public static TextureStageResult StagedToPersistentRing() => new(TextureStageOutcome.StagedToPersistentRing);
    public static TextureStageResult EnqueuedFallback() => new(TextureStageOutcome.EnqueuedFallback);
    public static TextureStageResult Rejected(TextureStageRejectReason reason) => new(TextureStageOutcome.Rejected, reason);
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
    TextureStreamingBackendKind Backend,
    TextureStreamingPriorityDiagnostics Priority,
    long StageCopyStaged,
    TextureStreamingFallbackDiagnostics Fallback);

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

    public static TextureUploadRegion For1DArray(int x, int layer, int width, int layerCount = 1, int mipLevel = 0)
        => new(x, layer, 0, width, layerCount, 1, mipLevel);

    public static TextureUploadRegion For2D(int x, int y, int width, int height, int mipLevel = 0)
        => new(x, y, 0, width, height, 1, mipLevel);

    public static TextureUploadRegion For2DArray(int x, int y, int layer, int width, int height, int layerCount = 1, int mipLevel = 0)
        => new(x, y, layer, width, height, layerCount, mipLevel);

    public static TextureUploadRegion ForCubeArrayFace(int x, int y, int cubeIndex, TextureCubeFace face, int width, int height, int mipLevel = 0)
        => new(x, y, TextureStreamingUtils.GetCubeArraySlice(cubeIndex, face), width, height, 1, mipLevel);

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
        GL.GetInteger(GetPName.NumExtensions, out int count);
        for (int i = 0; i < count; i++)
        {
            string ext = GL.GetString(StringNameIndexed.Extensions, i);
            if (ext == "GL_ARB_buffer_storage")
            {
                return true;
            }
        }

        return false;
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

    public static int GetCubeArraySlice(int cubeIndex, TextureCubeFace face)
        => checked(cubeIndex * 6 + (int)face);

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
