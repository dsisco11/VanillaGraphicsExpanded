using System;
using System.Buffers;
using System.Collections.Generic;
using System.Linq;
using VanillaGraphicsExpanded.LumOn.Scene.Geometry;

namespace VanillaGraphicsExpanded.LumOn.Scene;

/// <summary>Defers unavailable sources and admits finite priority-ordered retry sweeps without restarting progress.</summary>
internal sealed partial class LumonSceneFeedbackUpdateRenderer
{
    private readonly LumonSceneCaptureAdmission captureAdmission = new();
    private readonly Dictionary<ulong, RetryIdentity> captureRetries = new();
    private readonly Dictionary<ulong, RetryIdentity> captureSweepIdentities = new();
    private readonly Dictionary<ulong, long> visibleCapturePages = new();
    private TraceGeometryGpuScene? checkedRetryScene;
    private long captureRetryVersion, checkedRetryVersion = -1, checkedRetryGeometry = -1, checkedRetryTables = -1;

    #region Admission and ownership
    /// <summary>Records pending capture before dispatch so even a failed completion read retains a retry ticket.</summary>
    private bool TryAdmitCapture(LumonSceneCaptureWorkGpu item, bool retry)
    {
        if (item.ChunkSlot >= slotOwners.Length) return false;
        ulong key = LumonSceneVirtualPageKeyUtil.Pack(item.ChunkSlot, item.VirtualPageIndex);
        if (retry && (!captureSweepIdentities.TryGetValue(key, out var ticket) ||
            ticket.Physical != item.PhysicalPageId || !IsCurrentCapture(ticket.Physical, key, ticket.Generation))) return false;
        QueueCaptureRetry(item.PhysicalPageId, key);
        return captureAdmission.CanAdmit(item.PhysicalPageId, key, slotGenerations[item.ChunkSlot],
            slotOwners[item.ChunkSlot], item.PatchId);
    }

    /// <summary>Coalesces retry demand while retaining the physical page and slot lifetime that requested it.</summary>
    private void QueueCaptureRetry(uint physical, ulong key)
    {
        uint slot = LumonSceneVirtualPageKeyUtil.UnpackChunkSlot(key);
        if (slot >= slotGenerations.Length) return;
        var identity = new RetryIdentity(physical, slotGenerations[slot]);
        if (captureRetries.TryGetValue(key, out var old) && old == identity) return;
        captureRetries[key] = identity; captureRetryVersion++;
    }

    /// <summary>Rejects delayed demand after eviction or slot reuse, independently of physical ID reuse.</summary>
    private bool IsCurrentCapture(uint physical, ulong key, ushort generation)
    {
        uint slot = LumonSceneVirtualPageKeyUtil.UnpackChunkSlot(key);
        return slot < slotGenerations.Length && slotGenerations[slot] == generation &&
            virtualToPhysical.TryGetValue(key, out uint current) && current == physical;
    }
    #endregion

    #region Finite retry sweeps
    /// <summary>Rebuilds only after demand or publication changes; unavailable pages retain tickets without GPU admission.</summary>
    private void ResumeCaptureRetries()
    {
        var scene = traceGeometry?.PrepareScene();
        captureAdmission.SetScene(scene);
        if (recaptureVirtualPageKeys != null) return;
        if (ReferenceEquals(scene, checkedRetryScene) && checkedRetryGeometry == (scene?.Revision ?? -1) &&
            checkedRetryTables == (scene?.TablesRevision ?? -1) && checkedRetryVersion == captureRetryVersion) return;

        captureAdmission.Prune(IsCurrentCapture);
        captureSweepIdentities.Clear();
        ulong[] pages = ArrayPool<ulong>.Shared.Rent(Math.Max(1, captureRetries.Count));
        int count = 0;
        foreach (var pair in captureRetries.ToArray())
        {
            ulong key = pair.Key;
            var identity = pair.Value;
            if (!IsCurrentCapture(identity.Physical, key, identity.Generation))
            {
                captureRetries.Remove(key); captureRetryVersion++; continue;
            }
            uint slot = LumonSceneVirtualPageKeyUtil.UnpackChunkSlot(key);
            uint patch = LumonSceneVirtualPageKeyUtil.UnpackVirtualPageIndex(key);
            if (captureAdmission.CanAdmit(identity.Physical, key, identity.Generation, slotOwners[slot], patch))
            {
                pages[count++] = key;
                captureSweepIdentities[key] = identity;
            }
        }
        checkedRetryScene = scene; checkedRetryGeometry = scene?.Revision ?? -1;
        checkedRetryTables = scene?.TablesRevision ?? -1; checkedRetryVersion = captureRetryVersion;
        if (count == 0) { ArrayPool<ulong>.Shared.Return(pages, clearArray: false); return; }
        // Sort once per finite sweep. New demand cannot move its cursor back or starve later eligible pages.
        Array.Sort(pages, 0, count, Comparer<ulong>.Create(CompareCapturePriority));
        recaptureVirtualPageKeys = pages; recaptureCount = count; recaptureCursor = 0;
    }

    /// <summary>Prefers recently visible resident pages, then nearby source chunks, with deterministic ties.</summary>
    private int CompareCapturePriority(ulong left, ulong right)
    {
        long recent = lastNearPriorityContext.NowTick - 60;
        bool leftVisible = visibleCapturePages.TryGetValue(left, out long leftTick) && leftTick >= recent;
        bool rightVisible = visibleCapturePages.TryGetValue(right, out long rightTick) && rightTick >= recent;
        int visible = rightVisible.CompareTo(leftVisible);
        if (visible != 0) return visible;
        int distance = CaptureDistanceSquared(left).CompareTo(CaptureDistanceSquared(right));
        return distance != 0 ? distance : left.CompareTo(right);
    }

    /// <summary>Uses integer world chunk origins before double distance arithmetic, preserving signed large coordinates.</summary>
    private double CaptureDistanceSquared(ulong key)
    {
        var chunk = slotOwners[LumonSceneVirtualPageKeyUtil.UnpackChunkSlot(key)];
        var camera = lastNearPriorityContext.CameraBlockPos;
        double x = ((long)chunk.X << 5) + 16 - (long)camera.X;
        double y = ((long)chunk.Y << 5) + 16 - (long)camera.Y;
        double z = ((long)chunk.Z << 5) + 16 - (long)camera.Z;
        return x * x + y * y + z * z;
    }

    /// <summary>Retires pending capture and dependency caches at a resource lifetime boundary.</summary>
    private void ClearCaptureAdmission()
    {
        captureAdmission.Clear(); captureRetries.Clear(); captureSweepIdentities.Clear(); visibleCapturePages.Clear(); checkedRetryScene = null;
        captureRetryVersion = 0; checkedRetryVersion = checkedRetryGeometry = checkedRetryTables = -1;
    }
    #endregion

    /// <summary>Protects a queued virtual key against physical reassignment and chunk slot reuse.</summary>
    private readonly record struct RetryIdentity(uint Physical, ushort Generation);
}
