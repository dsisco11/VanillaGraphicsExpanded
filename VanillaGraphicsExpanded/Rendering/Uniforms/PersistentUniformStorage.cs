using System;
using System.Collections.Generic;
using OpenTK.Graphics.OpenGL;

namespace VanillaGraphicsExpanded.Rendering.Uniforms;

/// <summary>Pools immutable uniform versions and reclaims released slots after their latest frame fence.</summary>
internal sealed class PersistentUniformStorage : IDisposable
{
    private readonly List<UniformStoragePage> pages = new();
    private readonly Queue<(long Serial, GpuFence Fence)> fences = new();
    private readonly bool persistent;
    private readonly bool coherent;
    private readonly int alignment;
    private readonly int budget;
    private long serial = 1;
    private long completed;
    private bool used;
    private bool disposed;

    internal long AllocationsWritten { get; private set; }
    internal long BytesWritten { get; private set; }
    internal long Bindings { get; private set; }
    internal int ResidentBytes { get; private set; }
    internal long FencePolls { get; private set; }
    internal int PendingBytes
    {
        get
        {
            int result = 0;
            foreach (var page in pages)
                foreach (var version in page.Slots)
                    if (version?.Released == true) result += page.SlotSize;
            return result;
        }
    }

    #region Public API
    #region Allocation and provenance
    /// <summary>Uses bounded size-class pages; neither shader identity nor binding slot selects storage.</summary>
    internal PersistentUniformStorage(int alignment, bool persistent, bool coherent, int budget = 8 * 1024 * 1024)
    {
        if (alignment <= 0 || budget <= 0) throw new ArgumentOutOfRangeException(nameof(alignment));
        this.alignment = alignment; this.persistent = persistent; this.coherent = coherent; this.budget = budget;
    }

    /// <summary>Validates retained provenance without native state queries.</summary>
    internal bool IsCurrent(UniformStorageVersion version) => !disposed
        && ReferenceEquals(version.Owner, this) && !version.Released && version.Page.Buffer.IsValid
        && ReferenceEquals(version.Page.Slots[version.Slot], version) && version.Page.Generations[version.Slot] == version.Generation;

    /// <summary>Reserves a free aligned slot, copying complete contents before exposing the version.</summary>
    internal UniformStorageVersion Allocate(ReadOnlySpan<byte> data)
    {
        RequireAlive();
        if (data.Length <= 0 || data.Length > 65536) throw new ArgumentOutOfRangeException(nameof(data));
        Collect();
        int slotSize = alignment;
        while (slotSize < data.Length) slotSize = checked(slotSize * 2);
        UniformStoragePage? selected = null;
        int slot = -1;
        foreach (var page in pages)
        {
            if (page.SlotSize != slotSize) continue;
            slot = Array.FindIndex(page.Slots, value => value is null);
            if (slot >= 0) { selected = page; break; }
        }
        if (selected is null)
        {
            int capacity = checked(slotSize * Math.Max(8, 65536 / slotSize));
            if (capacity > budget - ResidentBytes) throw new InvalidOperationException("Persistent uniform storage budget exhausted.");
            selected = new UniformStoragePage(slotSize, capacity, persistent, coherent);
            pages.Add(selected); ResidentBytes += capacity; slot = 0;
        }
        var version = new UniformStorageVersion(this, selected, slot, data.Length);
        selected.Slots[slot] = version;
        try { selected.Write(version.Offset, data); }
        catch { selected.Slots[slot] = null; version.Released = true; throw; }
        AllocationsWritten++; BytesWritten += data.Length;
        return version;
    }

    #endregion
    #region Submission and retirement
    /// <summary>Records every successful publication, including unchanged rebindings and shader restoration.</summary>
    internal void MarkUsed(UniformStorageVersion version)
    {
        if (!IsCurrent(version)) throw new InvalidOperationException("Stale uniform version.");
        version.LastUse = serial;
        used = true;
        Bindings++;
    }

    /// <summary>Withdraws logical ownership; latest submitted use still protects the physical slot.</summary>
    internal void Release(UniformStorageVersion version)
    {
        if (!ReferenceEquals(version.Owner, this)) throw new ArgumentException("Foreign uniform version.");
        version.Released = true;
        // An unpublished reservation has no GPU readers and needs no fence or native work.
        if (version.LastUse == 0 && ReferenceEquals(version.Page.Slots[version.Slot], version))
            version.Page.Slots[version.Slot] = null;
    }

    /// <summary>Fences the latest uses as one batch before a frame boundary advances retirement authority.</summary>
    internal void SealFrame()
    {
        RequireAlive();
        if (used)
        {
            var fence = GpuFence.Insert();
            fences.Enqueue((serial, fence));
            serial++; used = false;
        }
        Collect();
    }

    /// <summary>Polls completion without waiting; only released versions behind completed work become free.</summary>
    internal void Collect()
    {
        RequireAlive();
        while (fences.TryPeek(out var batch))
        {
            FencePolls++;
            var status = batch.Fence.Poll();
            if (status == WaitSyncStatus.TimeoutExpired) break;
            if (status == WaitSyncStatus.WaitFailed) throw new InvalidOperationException("Uniform retirement fence failed.");
            completed = batch.Serial;
            batch.Fence.Dispose(); fences.Dequeue();
        }
        foreach (var page in pages)
            for (int i = 0; i < page.Slots.Length; i++)
                if (page.Slots[i] is { Released: true } version && version.LastUse <= completed)
                    page.Slots[i] = null;
    }

    /// <summary>Retires storage through normal GL deferred deletion before renderer context teardown.</summary>
    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        foreach (var batch in fences)
            batch.Fence.Dispose();
        foreach (var page in pages)
            page.Dispose();
        fences.Clear(); pages.Clear(); ResidentBytes = 0;
    }
    #endregion
    #endregion

    #region Private
    /// <summary>Prevents allocations or synchronization after renderer-owned storage is disposed.</summary>
    private void RequireAlive()
    {
        ObjectDisposedException.ThrowIf(disposed, this);
    }
    #endregion
}
