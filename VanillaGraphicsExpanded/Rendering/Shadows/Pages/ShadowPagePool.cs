using System;
using System.Collections.Generic;
using VanillaGraphicsExpanded.Rendering.RHI;
using VanillaGraphicsExpanded.Rendering.Shadows.Configuration;
using Vintagestory.API.Common;

namespace VanillaGraphicsExpanded.Rendering.Shadows.Pages;

/// <summary>Owns bounded GPU page assignments and one fixed-capacity depth array, without CPU selection readback.</summary>
internal sealed partial class ShadowPagePool : IDisposable
{
    private readonly ICoreAPI api;
    private readonly int availableSamplerSlots;
    private readonly IDisposable? reloadRegistration;
    private ShadowPageSettings policy = null!;
    private ShadowPageShader shader = null!;
    private RHIShaderRef executable;
    private ShadowTileClearShader clearShader = null!;
    private RHIShaderRef clearExecutable;
    private GpuShaderStorageBuffer commands = null!;
    private GpuTexture? depthArray;
    private readonly Queue<(long Epoch, GpuFence Fence)> fences = new();
    private long epoch, completed;
    private int operations;
    private bool batchOpen, withdrawn, disposed, resourcesAlive, writesFrozen;
    private bool completionReserved;
    private ShadowPageSettings? replacement;
    private readonly (nint Handle, long Generation) context;

    /// <summary>512-byte slots contain assignment, coverage, rendered projection and temporal state.</summary>
    internal GpuShaderStorageBuffer Entries { get; private set; } = null!;
    /// <summary>GPU result words: reason, slot, assignment low/high, followed by the assignment counter.</summary>
    internal GpuShaderStorageBuffer Results { get; private set; } = null!;
    /// <summary>Exact writable assignment and batch tokens; sampled completed pages never enter this mapping.</summary>
    internal GpuShaderStorageBuffer WritableAssignments { get; private set; } = null!;
    /// <summary>Identifies the current ordered submission interval for frozen rendering inputs.</summary>
    internal long Batch => epoch;
    /// <summary>Capacity covers every allocated tile across all array layers.</summary>
    internal int Capacity => checked((policy.TileColumns * policy.TileRows) * policy.ArrayLayers);
    /// <summary>Configured layer count; all layers reside in the same allocation.</summary>
    internal int ArrayLayers => policy.ArrayLayers;
    /// <summary>Reports all owned GPU bytes, including retired allocations pending fence completion.</summary>
    internal long AllocatedBytes => resourcesAlive ? MetadataBytes + DepthBytes : 0;
    /// <summary>Reports the withdrawn allocation until all submitted GPU users have finished.</summary>
    internal long DrainingBytes => withdrawn ? AllocatedBytes : 0;
    /// <summary>Exposes the immutable page extent used by projection and sampling.</summary>
    internal int PageSize => policy.PageSize;
    /// <summary>Exposes the frozen settings whose depth and bias conventions belong to this page generation.</summary>
    internal ShadowPageSettings Settings => policy.Freeze();
    /// <summary>Reports tile capacity per array layer without querying GPU selection.</summary>
    internal int TilesPerLayer => (policy.TileColumns * policy.TileRows);
    /// <summary>Bounds every shader lookup, including sampling misses.</summary>
    internal int ProbeLimit => Math.Min(policy.ProbeLimit, Capacity);
    /// <summary>False once publication is withdrawn or the original context has ended.</summary>
    internal bool IsAvailable => !disposed && !withdrawn;
    /// <summary>Sampling and draws must be submitted before this interval's retirement fence.</summary>
    internal bool CanSubmit => IsAvailable && batchOpen;
    /// <summary>True only before this submission interval has admitted or frozen replacement writes.</summary>
    internal bool CanPrepareReplacements => CanSubmit && !writesFrozen && !completionReserved;
    /// <summary>Reports the reserved whole-batch finalization obligation without observing GPU allocation results.</summary>
    internal bool ReplacementsPending => CanSubmit && completionReserved;
    /// <summary>Remaining command capacity must cover all completion publications before replacement tiles are cleared.</summary>
    internal int OperationsRemaining => CanSubmit ? policy.OperationsPerBatch - operations - (completionReserved ? 1 : 0) : 0;
    private long MetadataBytes => checked(Capacity * 528L + 576L + 32L);
    private long DepthBytes => checked(policy.PageSize * (long)policy.PageSize * 4 * Capacity);

    #region Public API
    #region Creation and operations
    /// <summary>Validates consumer binding availability and allocates the complete metadata and integer texture array.</summary>
    internal static ShadowPagePool Create(ICoreAPI api, ShadowPageSettings settings, int availableSamplerSlots = 1)
    {
        ArgumentNullException.ThrowIfNull(api); ArgumentNullException.ThrowIfNull(settings);
        return new(api, settings.Freeze(), availableSamplerSlots);
    }
    /// <summary>Opens a bounded submission interval; all consumers must finish submitting before SealBatch.</summary>
    internal bool BeginBatch()
    {
        ObjectDisposedException.ThrowIf(disposed, this); Poll();
        if (withdrawn || batchOpen || fences.Count >= 8 || epoch == long.MaxValue) return false;
        epoch++; operations = 0; writesFrozen = false; completionReserved = false; batchOpen = true; return true;
    }
    /// <summary>Queues one fixed operation; results remain on GPU for admission/drawing and test-only inspection.</summary>
    internal bool Execute(ShadowPageRequest request)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        if (!CanSubmit || OperationsRemaining == 0) return false;
        if (request.Projection is { } projection && projection.PageSize != PageSize)
            throw new ArgumentException("Rendered projection and physical tile sizes disagree.", nameof(request));
        if (writesFrozen && request.Operation is ShadowPageOperation.Allocate or ShadowPageOperation.CancelReplacement) return false;
        if (request.Operation is ShadowPageOperation.FreezeWrites or ShadowPageOperation.FinishReplacements)
            throw new ArgumentException("Use the typed batch freeze/completion operations.", nameof(request));
        uint[] packed = request.Pack();
        if (request.Operation == ShadowPageOperation.Allocate && !ReserveCompletion()) return false;
        operations++;
        Dispatch(packed); return true;
    }
    /// <summary>Consumes a GPU-produced completion request without reading its assignment or admission result on the CPU.</summary>
    internal bool ExecuteGenerated(RHIBufferRange request)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        if (!CanSubmit || OperationsRemaining == 0) return false;
        if (request.Length != 512 || request.Offset < 0 || request.Offset % 4 != 0)
            throw new ArgumentException("A generated page command is exactly 512 aligned bytes.", nameof(request));
        operations++;
        Dispatch(null, request);
        return true;
    }
    /// <summary>Admits replacement storage only when the GPU has certified complete bounded geometry selection.</summary>
    internal bool AllocateSelected(ShadowPageRequest request, RHIBufferRange certificate)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        if (!CanSubmit || writesFrozen || OperationsRemaining == 0) return false;
        if (request.Operation != ShadowPageOperation.Allocate || request.Projection is not { } projection ||
            projection.PageSize != PageSize || certificate.Length != 12 || certificate.Offset < 0 || certificate.Offset % 4 != 0)
            throw new ArgumentException("Selected allocation requires a projected allocation and aligned GPU success/epoch words.");
        uint[] packed = request.Pack();
        if (!ReserveCompletion()) return false;
        operations++;
        Dispatch(packed, certificate: certificate);
        return true;
    }
    /// <summary>Freezes replacement tokens before rendering; uncleared tiles remain forbidden by the fragment guard.</summary>
    internal bool FreezeWritableAssignments()
    {
        if (!CanSubmit || writesFrozen || OperationsRemaining == 0) return false;
        operations++;
        Dispatch(new ShadowPageRequest(ShadowPageOperation.FreezeWrites, default, default, 0, 0, 0).Pack());
        writesFrozen = true;
        return true;
    }
    /// <summary>Inserts a lifetime fence after all scheduled page users; no current-frame completion readback occurs.</summary>
    internal void SealBatch()
    {
        if (!batchOpen) throw new InvalidOperationException("No open shadow page batch.");
        if (completionReserved) FinishReplacements(false);
        fences.Enqueue((epoch, RHICommandContext.Current.InsertFence())); batchOpen = false;
    }
    /// <summary>Borrows the complete resident depth array; ownership remains with this pool.</summary>
    internal GpuTexture DepthArray => IsAvailable && depthArray is not null
        ? depthArray : throw new InvalidOperationException("Depth array is unavailable.");
    #endregion
    #region Capacity and lifetime
    /// <summary>Withdraws before validating a replacement; invalid layouts stay unavailable and two full pools never coexist.</summary>
    internal void Reconfigure(ShadowPageSettings settings)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        ArgumentNullException.ThrowIfNull(settings);
        if (batchOpen) throw new InvalidOperationException("Reconfigure only after all current consumers have submitted.");
        // A rejected effective configuration cannot keep old capacities published as current.
        // Drain those resources even when validation prevents a replacement allocation.
        Withdraw();
        var next = settings.Freeze(); Validate(next, availableSamplerSlots);
        replacement = next;
    }
    /// <summary>Withdraws publications on disable or world leave, retaining storage until submitted GPU users complete.</summary>
    internal void Withdraw()
    {
        // A newer disable/world-leave/reload supersedes any queued attempt to recreate storage.
        replacement = null;
        if (disposed || withdrawn) return;
        // Withdrawal clears the entire table below. Do not invoke a completion
        // executable which may itself be the reason this pool was withdrawn.
        completionReserved = false;
        if (batchOpen) SealBatch();
        // A clear is ordered after prior consumers and prevents any later key lookup from certifying old pages.
        Entries.UploadSubData<uint>(new uint[checked(Capacity * 128)], 0);
        if (epoch < long.MaxValue) epoch++;
        fences.Enqueue((epoch, RHICommandContext.Current.InsertFence())); withdrawn = true;
    }
    /// <summary>Polls bounded fences and completes explicit replacement after the old allocation drains.</summary>
    internal void Poll()
    {
        if (disposed) return;
        while (fences.TryPeek(out var pending) && pending.Fence.TryConsumeIfSignaled())
        { completed = pending.Epoch; fences.Dequeue(); }
        if (withdrawn && fences.Count == 0 && (resourcesAlive || replacement is not null))
        {
            ReleaseResources();
            if (replacement is { } next)
            {
                replacement = null;
                try { Initialize(next); withdrawn = false; }
                catch { withdrawn = true; ReleaseResources(); throw; }
            }
        }
    }
    /// <summary>Ends ownership through RHI retirement; live reconfiguration uses Withdraw/Poll to retain tracked bytes.</summary>
    public void Dispose()
    {
        if (disposed) return;
        withdrawn = true; disposed = true;
        Integration.RenderContextRegistry.Retiring -= OnContextRetiring;
        reloadRegistration?.Dispose();
        ReleaseResources();
        while (fences.TryDequeue(out var pending)) pending.Fence.Dispose();
    }
    #endregion
    #endregion

    #region Private
    /// <summary>Reserves cancellation before allocating so exhaustion cannot strand unfinished replacement tiles.</summary>
    private bool ReserveCompletion()
    {
        if (completionReserved) return true;
        if (OperationsRemaining < 2) return false;
        completionReserved = true;
        return true;
    }
    /// <summary>Binds the generated shader contract and establishes storage visibility after each command.</summary>
    private void Dispatch(uint[]? packed, RHIBufferRange? generated = null, RHIBufferRange? certificate = null)
    {
        if (!shader.EnsureReady() || shader.Shader != executable)
        { Withdraw(); throw new InvalidOperationException("Page shader replacement requires pool reconfiguration."); }
        uint[] prefix = new uint[16]; prefix[0] = (uint)Capacity;
        prefix[2] = (uint)ProbeLimit;
        ShadowPageRequest.Put(prefix, 4, epoch); ShadowPageRequest.Put(prefix, 6, completed);
        prefix[8] = (uint)TilesPerLayer; prefix[9] = (uint)policy.TileColumns;
        prefix[10] = (uint)PageSize;
        prefix[12] = writesFrozen ? 1u : 0u;
        prefix[13] = generated.HasValue ? 1u : 0u;
        prefix[15] = certificate.HasValue ? 1u : 0u;
        commands.UploadSubData<uint>(prefix, 0);
        if (!generated.HasValue) commands.UploadSubData<uint>(packed!, 64);
        var bindings = (IShadowPageBindings)shader; bindings.Commands = commands; bindings.Entries = Entries; bindings.Results = Results;
        bindings.Writable = WritableAssignments;
        var rhi = RHICommandContext.Current;
        using var boundary = rhi.EnterBoundary(new("Shadows.Pages", true, compute: [shader.Pipeline]));
        boundary.Run(() => {
            rhi.Barrier(new(ResourceAccess.Image | ResourceAccess.ShaderStorage | ResourceAccess.TextureFetch | ResourceAccess.BufferUpdate));
            if (generated is { } source)
                rhi.CopyBuffer(source.Buffer, source.Offset, commands, 64, 512);
            if (certificate is { } selected)
            {
                rhi.CopyBuffer(selected.Buffer, selected.Offset, commands, 56, 4);
                rhi.CopyBuffer(selected.Buffer, selected.Offset + 4, commands, 64 + 126 * 4, 8);
            }
            shader.Dispatch(1);
            rhi.Barrier(new(ResourceAccess.ShaderStorage | ResourceAccess.BufferUpdate | ResourceAccess.TextureFetch));
        });
    }
    /// <summary>Withdraws immediately at the originating context's teardown boundary.</summary>
    private void OnContextRetiring((nint Handle, long Generation) origin) { if (origin == context) Dispose(); }
    #endregion
}
