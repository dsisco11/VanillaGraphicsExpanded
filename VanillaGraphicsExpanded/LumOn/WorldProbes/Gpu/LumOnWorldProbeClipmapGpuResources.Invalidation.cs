using VanillaGraphicsExpanded.Rendering.ShaderCompilation;
using VanillaGraphicsExpanded.Rendering.RHI;
using System;
using System.Collections.Generic;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Numerics;
using Vintagestory.API.Common;

namespace VanillaGraphicsExpanded.LumOn.WorldProbes.Gpu;

/// <summary>Batches changed regions into unique physical slots for compute-based history retirement.</summary>
internal sealed partial class LumOnWorldProbeClipmapGpuResources
{
    private WorldProbeHistoryClearShader? historyClear;
    private GpuShaderStorageBuffer? historyClearSlots;
    private bool[] queuedHistorySlots = Array.Empty<bool>();
    private readonly List<int> historySlots = new();
    private bool clearAllHistory;

    /// <summary>Number of unique physical slots awaiting invalidation.</summary>
    internal int PendingInvalidationCount => clearAllHistory ? queuedHistorySlots.Length : historySlots.Count;

    /// <summary>Number of submitted clear dispatches, including initialization and full resets.</summary>
    internal long InvalidationDispatchCount { get; private set; }

    #region History invalidation

    /// <summary>Loads the production compute program before any probe history is made available.</summary>
    private void InitializeHistoryInvalidation(ICoreAPI api)
    {
        queuedHistorySlots = new bool[checked(levels * resolution * resolution * resolution)];
        if (!ComputeShaderAsset.TryCreateFromAssets(api: api,
            settings: new global::VanillaGraphicsExpanded.Rendering.Contracts.ShaderSettings(global::VanillaGraphicsExpanded.Rendering.Contracts.GpuShaderContracts.Registry.FindProgram(WorldProbeHistoryClearShader.Contract.Identity)),
            pipeline: out var created,
            infoLog: out string log))
            throw new InvalidOperationException("Cannot initialize world-probe history: " + log);
        historyClear = new(created!);
        historyClearSlots = RHICommandContext.Current.CreateShaderStorageBuffer(new RHIBufferDescription { Role = BufferRole.ShaderStorage, Mode = ResourceStorageMode.Mutable, Capacity = 0, Usage = BufferUsage.StreamDraw, Label = null }).RequireResource();
    }

    /// <summary>Queues an inclusive local box; overlapping regions share one physical-slot entry.</summary>
    public void QueueClearLocalBox(int level, VectorInt3 ring, VectorInt3 min, VectorInt3 max)
    {
        if (clearAllHistory) return;
        // Resolve the ring now: later anchor changes must not reinterpret an already queued region.
        for (int z = min.Z; z <= max.Z; z++)
        for (int y = min.Y; y <= max.Y; y++)
        for (int x = min.X; x <= max.X; x++)
        {
            int sx = (x + ring.X) % resolution;
            int sy = (y + ring.Y) % resolution;
            int sz = (z + ring.Z) % resolution;
            int slot = ((level * resolution + sz) * resolution + sy) * resolution + sx;
            if (queuedHistorySlots[slot]) continue;
            queuedHistorySlots[slot] = true;
            historySlots.Add(slot);
        }
    }

    /// <summary>Clears queued slots once, before atlas uploads and subsequent texture consumers.</summary>
    public void FlushHistoryInvalidation()
    {
        int count = PendingInvalidationCount;
        if (count == 0) return;
        if(!historyClear!.EnsureReady())throw new InvalidOperationException(historyClear.PreparationLog);
        int minimum=historyClear.ProgramLayout.BinaryInterface!.PreparedBindings.Resolve(
            global::VanillaGraphicsExpanded.Rendering.Contracts.GpuBindingEntry.Identity(
                global::VanillaGraphicsExpanded.Rendering.Contracts.ShaderBindingKind.StorageBlock,"HistoryClearSlots")).MinimumSize;
        // The runtime array still contributes to the linked block minimum during a whole-history clear.
        int[] descriptors = new int[Math.Max(clearAllHistory ? 4 : checked(count + 4),(minimum + 3) / 4)];
        descriptors[0] = count;
        descriptors[1] = resolution;
        descriptors[2] = worldProbeTileSize;
        descriptors[3] = clearAllHistory ? 1 : 0;
        if (!clearAllHistory) historySlots.CopyTo(descriptors, 4);
        int bytes = checked(descriptors.Length << 2);
        historyClearSlots!.EnsureCapacity(bytes);
        historyClearSlots.UploadSubData<int>(descriptors, 0, bytes);
        historyClear!.HistoryClearSlots = new(historyClearSlots, 0, bytes);
        historyClear.radianceAtlas = new(radianceAtlas, ImageAccess.WriteOnly, Format: TextureFormat.Rgba16f);
        historyClear.visibilityAtlas = new(vis0, ImageAccess.WriteOnly, Format: TextureFormat.Rgba16f);
        historyClear.distanceAtlas = new(dist0, ImageAccess.WriteOnly, Format: TextureFormat.Rg16f);
        historyClear.metadataAtlas = new(meta0, ImageAccess.WriteOnly, Format: TextureFormat.Rg32f);
        // Use a second dispatch dimension when the slot count exceeds GL's guaranteed X limit.
        int groupsX = Math.Min(count, 65535);
        var dispatchContext=RHICommandContext.Current;
        using var dispatchBoundary=dispatchContext.EnterBoundary(new("WorldProbe.Invalidate",true,compute:[historyClear.Pipeline]));
        dispatchBoundary.Run(()=>historyClear.Dispatch(groupsX, (count + groupsX - 1) / groupsX, 1));
        RHICommandContext.Current.Barrier(new(ResourceAccess.Image | ResourceAccess.TextureFetch |
            ResourceAccess.Framebuffer | ResourceAccess.TextureUpdate));
        InvalidationDispatchCount++;
        foreach (int slot in historySlots) queuedHistorySlots[slot] = false;
        historySlots.Clear();
        clearAllHistory = false;
    }

    /// <summary>Retires every slot immediately, superseding pending regional invalidations.</summary>
    public void ClearAll()
    {
        clearAllHistory = true;
        FlushHistoryInvalidation();
    }

    #endregion
}
