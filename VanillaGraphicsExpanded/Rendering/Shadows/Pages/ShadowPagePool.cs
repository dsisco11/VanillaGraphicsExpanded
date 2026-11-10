using System;
using System.Collections.Generic;
using VanillaGraphicsExpanded.Rendering.RHI;
using VanillaGraphicsExpanded.Rendering.Shadows.Configuration;
using Vintagestory.API.Common;

namespace VanillaGraphicsExpanded.Rendering.Shadows.Pages;

/// <summary>Owns bounded GPU page assignments and resident depth arrays, without CPU selection readback.</summary>
internal sealed partial class ShadowPagePool : IDisposable
{
    private readonly ICoreAPI api;
    private readonly int availableSamplerSlots;
    private readonly IDisposable? reloadRegistration;
    private ShadowPageSettings policy = null!;
    private ShadowPageShader shader = null!;
    private RHIShaderRef executable;
    private GpuShaderStorageBuffer commands = null!;
    private readonly List<GpuTexture> arrays = new();
    private readonly Queue<(long Epoch, GpuFence Fence)> fences = new();
    private long epoch, completed, shrinkEpoch;
    private int targetArrays, operations;
    private bool batchOpen, withdrawn, disposed, resourcesAlive;
    private ShadowPageSettings? replacement;
    private readonly (nint Handle, long Generation) context;

    /// <summary>256-byte slots contain physical assignment, coverage, dependencies and bounded queue state.</summary>
    internal GpuShaderStorageBuffer Entries { get; private set; } = null!;
    /// <summary>GPU result words: reason, slot, assignment low/high, followed by the assignment counter.</summary>
    internal GpuShaderStorageBuffer Results { get; private set; } = null!;
    /// <summary>Capacity includes unallocated array groups; a request cannot allocate a nonresident layer.</summary>
    internal int Capacity => checked(policy.LayersPerArray * policy.ArrayCapacity);
    /// <summary>Current physically resident array groups, including groups draining after a shrink.</summary>
    internal int ResidentArrays => arrays.Count;
    /// <summary>Reports all owned GPU bytes, including retired allocations pending fence completion.</summary>
    internal long AllocatedBytes => resourcesAlive ? MetadataBytes + arrays.Count * ArrayBytes : 0;
    /// <summary>Reports the still-resident excess during a shrink instead of hiding retirement cost.</summary>
    internal long DrainingBytes => Math.Max(0, arrays.Count - targetArrays) * ArrayBytes;
    /// <summary>Exposes the immutable page extent used by projection and sampling.</summary>
    internal int PageSize => policy.PageSize;
    /// <summary>Maps a GPU slot to an array group and layer without querying selection.</summary>
    internal int LayersPerArray => policy.LayersPerArray;
    /// <summary>Bounds every shader lookup, including sampling misses.</summary>
    internal int ProbeLimit => Math.Min(policy.ProbeLimit, Capacity);
    /// <summary>False once publication is withdrawn or the original context has ended.</summary>
    internal bool IsAvailable => !disposed && !withdrawn;
    /// <summary>Sampling and draws must be submitted before this interval's retirement fence.</summary>
    internal bool CanSubmit => IsAvailable && batchOpen;
    /// <summary>Remaining command capacity must cover all completion publications before replacement layers are cleared.</summary>
    internal int OperationsRemaining => CanSubmit ? policy.OperationsPerBatch - operations : 0;
    private long MetadataBytes => checked(Capacity * 256L + 320L + 32L);
    private long ArrayBytes => checked(policy.PageSize * (long)policy.PageSize * 4 * policy.LayersPerArray);

    #region Public API
    #region Creation and operations
    /// <summary>Validates planned compute/fragment binding availability and allocates metadata, then one depth array.</summary>
    internal static ShadowPagePool Create(ICoreAPI api, ShadowPageSettings settings, int availableSamplerSlots = 4)
    {
        ArgumentNullException.ThrowIfNull(api); ArgumentNullException.ThrowIfNull(settings);
        return new(api, settings.Freeze(), availableSamplerSlots);
    }
    /// <summary>Opens a bounded submission interval; all consumers must finish submitting before SealBatch.</summary>
    internal bool BeginBatch()
    {
        ObjectDisposedException.ThrowIf(disposed, this); Poll();
        if (withdrawn || batchOpen || fences.Count >= 8 || epoch == long.MaxValue) return false;
        epoch++; operations = 0; batchOpen = true; return true;
    }
    /// <summary>Queues one fixed operation; results remain on GPU for admission/drawing and test-only inspection.</summary>
    internal bool Execute(ShadowPageRequest request)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        if (!batchOpen || withdrawn || operations >= policy.OperationsPerBatch) return false;
        uint[] packed = request.Pack(); operations++;
        Dispatch(packed); return true;
    }
    /// <summary>Consumes a GPU-produced completion request without reading its assignment or admission result on the CPU.</summary>
    internal bool ExecuteGenerated(RHIBufferRange request)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        if (!CanSubmit || OperationsRemaining == 0) return false;
        if (request.Length != 256 || request.Offset < 0 || request.Offset % 4 != 0)
            throw new ArgumentException("A generated page command is exactly 256 aligned bytes.", nameof(request));
        operations++;
        Dispatch(null, request);
        return true;
    }
    /// <summary>Inserts a lifetime fence after all scheduled page users; no current-frame completion readback occurs.</summary>
    internal void SealBatch()
    {
        if (!batchOpen) throw new InvalidOperationException("No open shadow page batch.");
        fences.Enqueue((epoch, RHICommandContext.Current.InsertFence())); batchOpen = false;
    }
    /// <summary>Returns resident storage for an array submission group; ownership remains with this pool.</summary>
    internal GpuTexture DepthArray(int index)
    {
        if (!IsAvailable || index < 0 || index >= arrays.Count) throw new InvalidOperationException("Depth array is unavailable.");
        return arrays[index];
    }
    #endregion
    #region Capacity and lifetime
    /// <summary>Grows by one array per call, with all metadata and resident/retired bytes charged before admission.</summary>
    internal bool Grow()
    {
        if (!IsAvailable || batchOpen || arrays.Count >= targetArrays || AllocatedBytes + ArrayBytes > policy.MemoryMiB * 1048576L) return false;
        arrays.Add(RHICommandContext.Current.CreateTexture(new RHITextureDescription {
            Dimension = TextureDimension.TwoArray, Format = TextureFormat.DepthComponent32f,
            Width = policy.PageSize, Height = policy.PageSize, Depth = policy.LayersPerArray, MipLevels = 1,
            Storage = ResourceStorageMode.Immutable, Filter = TextureFilterMode.Nearest, BaseLevel = 0, MaxLevel = 0,
            Label = "Shadows.Depth." + arrays.Count }).RequireResource());
        return true;
    }
    /// <summary>Drains excess groups at a boundary; growth remains incremental and cannot bypass the memory allowance.</summary>
    internal void SetArrayTarget(int count)
    {
        if (!IsAvailable || batchOpen || count < 1 || count > policy.ArrayCapacity) throw new ArgumentOutOfRangeException(nameof(count));
        if (count < arrays.Count)
        {
            if (!BeginBatch()) throw new InvalidOperationException("Page retirement queue is full.");
            targetArrays = count;
            Dispatch(new ShadowPageRequest(ShadowPageOperation.Collect, default, default, 0, 0, 0).Pack());
            shrinkEpoch = epoch; SealBatch();
        }
        else targetArrays = count;
    }
    /// <summary>Withdraws old publications before a format/layout/policy replacement; two full pools never coexist.</summary>
    internal void Reconfigure(ShadowPageSettings settings)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        if (batchOpen) throw new InvalidOperationException("Reconfigure only after all current consumers have submitted.");
        var next = settings.Freeze(); Validate(next, availableSamplerSlots);
        Withdraw(); replacement = next;
    }
    /// <summary>Withdraws publications on disable or world leave, retaining storage until submitted GPU users complete.</summary>
    internal void Withdraw()
    {
        // A newer disable/world-leave/reload supersedes any queued attempt to recreate storage.
        replacement = null;
        if (disposed || withdrawn) return;
        if (batchOpen) SealBatch();
        // A clear is ordered after prior consumers and prevents any later key lookup from certifying old pages.
        Entries.UploadSubData<uint>(new uint[checked(Capacity * 64)], 0);
        if (epoch < long.MaxValue) epoch++;
        fences.Enqueue((epoch, RHICommandContext.Current.InsertFence())); withdrawn = true;
    }
    /// <summary>Polls bounded fences, drains shrink excess and completes requested replacement without blocking.</summary>
    internal void Poll()
    {
        if (disposed) return;
        while (fences.TryPeek(out var pending) && pending.Fence.TryConsumeIfSignaled())
        { completed = pending.Epoch; fences.Dequeue(); }
        if (shrinkEpoch > 0 && completed >= shrinkEpoch)
        {
            while (arrays.Count > targetArrays) { arrays[^1].Dispose(); arrays.RemoveAt(arrays.Count - 1); }
            shrinkEpoch = 0;
        }
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
    /// <summary>Binds the generated shader contract and establishes storage visibility after each command.</summary>
    private void Dispatch(uint[]? packed, RHIBufferRange? generated = null)
    {
        if (!shader.EnsureReady() || shader.Shader != executable)
        { Withdraw(); throw new InvalidOperationException("Page shader replacement requires pool reconfiguration."); }
        uint[] prefix = new uint[16]; prefix[0] = (uint)Capacity; prefix[1] = (uint)(arrays.Count * policy.LayersPerArray);
        prefix[2] = (uint)ProbeLimit; prefix[3] = (uint)(targetArrays * policy.LayersPerArray);
        ShadowPageRequest.Put(prefix, 4, epoch); ShadowPageRequest.Put(prefix, 6, completed);
        commands.UploadSubData<uint>(prefix, 0);
        if (generated is { } source)
            RHICommandContext.Current.CopyBuffer(source.Buffer, source.Offset, commands, 64, 256);
        else commands.UploadSubData<uint>(packed!, 64);
        var bindings = (IShadowPageBindings)shader; bindings.Commands = commands; bindings.Entries = Entries; bindings.Results = Results;
        var rhi = RHICommandContext.Current;
        using var boundary = rhi.EnterBoundary(new("Shadows.Pages", true, compute: [shader.Pipeline]));
        boundary.Run(() => { shader.Dispatch(1); rhi.Barrier(new(ResourceAccess.ShaderStorage | ResourceAccess.BufferUpdate | ResourceAccess.TextureFetch)); });
    }
    /// <summary>Withdraws immediately at the originating context's teardown boundary.</summary>
    private void OnContextRetiring((nint Handle, long Generation) origin) { if (origin == context) Dispose(); }
    #endregion
}
