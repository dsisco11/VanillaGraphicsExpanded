using System;
using System.Collections.Generic;
using System.Linq;
using VanillaGraphicsExpanded.LumOn.Scene.Geometry;
using VanillaGraphicsExpanded.Numerics;

namespace VanillaGraphicsExpanded.LumOn.Scene;

/// <summary>Caches CPU capture eligibility by physical identity and the source's publication dependencies.</summary>
internal sealed class LumonSceneCaptureAdmission
{
    private readonly Dictionary<uint, Entry> entries = new();
    private TraceGeometryGpuScene? scene;
    public long Checks { get; private set; }
    public long Deferred { get; private set; }
    public int Count => entries.Count;

    #region Admission
    /// <summary>Forgets dependency tokens when their owning GPU scene is replaced.</summary>
    public void SetScene(TraceGeometryGpuScene? current)
    {
        if (ReferenceEquals(scene, current)) return;
        entries.Clear(); scene = current;
    }

    /// <summary>Rechecks source texels only when identity, coverage membership or published inputs change.</summary>
    public bool CanAdmit(uint physical, ulong key, ushort generation, VectorInt3 chunk, uint patchId)
    {
        if (scene == null || !TraceGeometryCapturePatch.TryCreate(chunk, patchId, out var patch))
        { Deferred++; return false; }
        var stamp = scene.CaptureStamp(patch);
        if (!entries.TryGetValue(physical, out var entry) || entry.Key != key || entry.Generation != generation ||
            entry.Chunk != chunk || entry.Patch != patchId || entry.Stamp != stamp)
        {
            Checks++;
            entry = new(key, generation, chunk, patchId, stamp, scene.CanCapture(patch));
            entries[physical] = entry;
        }
        if (!entry.Available) Deferred++;
        return entry.Available;
    }

    /// <summary>Retains a rejected attempt until source dependencies change rather than resubmitting the same inputs.</summary>
    public void Reject(uint physical)
    {
        if (entries.TryGetValue(physical, out var entry)) entries[physical] = entry with { Available = false };
    }

    /// <summary>Bounds cached records to current physical ownership, including slot generation.</summary>
    public void Prune(Func<uint, ulong, ushort, bool> current)
    {
        foreach (var pair in entries.ToArray())
            if (!current(pair.Key, pair.Value.Key, pair.Value.Generation)) entries.Remove(pair.Key);
    }

    /// <summary>Releases scene references and world-specific counters on leave or pool replacement.</summary>
    public void Clear() { entries.Clear(); scene = null; Checks = Deferred = 0; }
    #endregion

    /// <summary>Contains no voxel copies or GPU handles; one record belongs to one current physical page.</summary>
    private readonly record struct Entry(ulong Key, ushort Generation, VectorInt3 Chunk, uint Patch,
        TraceGeometryCaptureStamp Stamp, bool Available);
}
