using System;
using System.Collections.Generic;
using VanillaGraphicsExpanded.Rendering;

namespace VanillaGraphicsExpanded.LumOn.Scene;

/// <summary>Allocates independent lighting work while preserving priority, fairness and bounded publication.</summary>
internal sealed partial class LumonSceneRelightUpdateRenderer
{
    private readonly SurfaceLightingAdmissionSchedule seedAdmission = new(), directAdmission = new(), indirectAdmission = new();
    private readonly GpuShaderStorageBuffer?[] operationBuffers = new GpuShaderStorageBuffer?[3];
    private readonly HashSet<uint> publicationSelection = new();
    private readonly Dictionary<uint, int> changedDirectPages = new();
    private long directInvalidation = -1;

    #region Work selection
    /// <summary>Reserves seeding independently and shares each refresh allocation between priority and background pages.</summary>
    private void SelectLightingWork(SurfaceLightingBudgets budgets, int batchCount)
    {
        seedWork.Clear(); traceWork.Clear(); refreshWork.Clear(); resetWork.Clear();
        if (directInvalidation != scene!.InvalidationRevision)
        {
            // Source invalidation is scene-wide: conservatively prioritize one direct sweep for resident lighting.
            directInvalidation = scene.InvalidationRevision;
            foreach (uint id in candidates)
                if (publishedPages.Contains(id)) changedDirectPages[id] = batchCount;
        }
        var priority = (uint[])candidates.Clone();
        Array.Sort(priority, (left, right) => feedback.CompareCapturePriority(identities[left], identities[right]));
        seedAdmission.Select(candidates, priority, budgets.Seed, id =>
        {
            if (seeded.Contains(id)) return false;
            var work = CreateLightingWork(id, batches.Next(identities[id], batchCount));
            seedWork.Add(work);
            if (!initialized.Contains(id)) resetWork.Add(work);
            return true;
        });
        Array.Sort(priority, (left, right) =>
        {
            int changed = changedDirectPages.ContainsKey(right).CompareTo(changedDirectPages.ContainsKey(left));
            return changed != 0 ? changed : feedback.CompareCapturePriority(identities[left], identities[right]);
        });
        directAdmission.Select(candidates, priority, budgets.Direct, id =>
        {
            if (!publishedPages.Contains(id)) return false;
            refreshSchedule.TryNext(id, false, batchCount, frame, out uint bucket);
            refreshWork.Add(CreateLightingWork(id, bucket));
            return true;
        });
        indirectAdmission.Select(candidates, priority, budgets.Indirect, id =>
        {
            if (!publishedPages.Contains(id) || !refreshSchedule.TryNext(id, true, batchCount, frame, out uint bucket)) return false;
            traceWork.Add(CreateLightingWork(id, bucket));
            return true;
        });
    }

    /// <summary>Builds a shader descriptor from the current surface identity and independent bucket cursor.</summary>
    private LumonSceneRelightWorkGpu CreateLightingWork(uint id, uint bucket)
    {
        ulong key = identities[id];
        return new(id, LumonSceneVirtualPageKeyUtil.UnpackChunkSlot(key), bucket,
            LumonSceneVirtualPageKeyUtil.UnpackVirtualPageIndex(key));
    }

    /// <summary>Combines each physical tile once even when several operations produced progress in the same frame.</summary>
    private void DeduplicatePublication()
    {
        publicationSelection.Clear();
        int write = 0;
        for (int i = 0; i < publishable.Count; i++)
            if (publicationSelection.Add(publishable[i].PhysicalPageId)) publishable[write++] = publishable[i];
        if (write < publishable.Count) publishable.RemoveRange(write, publishable.Count - write);
    }

    /// <summary>Restarts admission cursors only when the lighting resource lifetime changes.</summary>
    private void ResetLightingAdmission()
    {
        seedAdmission.Clear(); directAdmission.Clear(); indirectAdmission.Clear(); publicationSelection.Clear();
        changedDirectPages.Clear(); directInvalidation = -1;
    }
    #endregion
}
