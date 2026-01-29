using System;
using System.Collections.Generic;

namespace VanillaGraphicsExpanded.LumOn.Scene;

[Flags]
internal enum LumonScenePatchDirtyFlags : ushort
{
    None = 0,
    Capture = 1 << 0,
    Relight = 1 << 1,
    Material = 1 << 2,
}

internal sealed class LumonScenePatchRegistry
{
    private struct PatchRecord
    {
        public LumonScenePatchKey Key;
        public int LastSeenRemeshSequence;
        public LumonScenePatchDirtyFlags DirtyFlags;
    }

    private readonly Dictionary<LumonScenePatchKey, LumonScenePatchId> idByKey = new();
    private readonly List<PatchRecord> records = new();
    private readonly List<LumonScenePatchId> activePatchIds = new();

    private int remeshSequence = int.MinValue;

    public IReadOnlyList<LumonScenePatchId> ActivePatchIds => activePatchIds;

    public int TotalPatchIdCount => records.Count;

    public bool TryGetId(in LumonScenePatchKey key, out LumonScenePatchId id)
        => idByKey.TryGetValue(key, out id);

    public void BeginRemesh(int newRemeshSequence)
    {
        if (newRemeshSequence == int.MinValue)
        {
            throw new ArgumentOutOfRangeException(nameof(newRemeshSequence));
        }

        remeshSequence = newRemeshSequence;
        activePatchIds.Clear();
    }

    public LumonScenePatchId GetOrCreate(in LumonScenePatchKey key)
    {
        if (remeshSequence == int.MinValue)
        {
            throw new InvalidOperationException("BeginRemesh must be called before registering patches.");
        }

        if (!idByKey.TryGetValue(key, out LumonScenePatchId id))
        {
            id = new LumonScenePatchId(records.Count);
            idByKey.Add(key, id);
            records.Add(new PatchRecord
            {
                Key = key,
                LastSeenRemeshSequence = remeshSequence,
                DirtyFlags = LumonScenePatchDirtyFlags.Capture | LumonScenePatchDirtyFlags.Relight | LumonScenePatchDirtyFlags.Material,
            });

            activePatchIds.Add(id);
            return id;
        }

        int idx = id.Value;
        PatchRecord rec = records[idx];

        if (rec.LastSeenRemeshSequence != remeshSequence)
        {
            rec.LastSeenRemeshSequence = remeshSequence;
            records[idx] = rec;
            activePatchIds.Add(id);
        }

        return id;
    }

    public bool TryGetKey(LumonScenePatchId id, out LumonScenePatchKey key)
    {
        int idx = id.Value;
        if ((uint)idx >= (uint)records.Count)
        {
            key = default;
            return false;
        }

        key = records[idx].Key;
        return true;
    }

    public LumonScenePatchDirtyFlags GetDirtyFlags(LumonScenePatchId id)
    {
        int idx = id.Value;
        if ((uint)idx >= (uint)records.Count)
        {
            return LumonScenePatchDirtyFlags.None;
        }

        return records[idx].DirtyFlags;
    }

    public void MarkDirty(LumonScenePatchId id, LumonScenePatchDirtyFlags flags)
    {
        int idx = id.Value;
        if ((uint)idx >= (uint)records.Count)
        {
            return;
        }

        PatchRecord rec = records[idx];
        rec.DirtyFlags |= flags;
        records[idx] = rec;
    }

    public void MarkDirty(in LumonScenePatchKey key, LumonScenePatchDirtyFlags flags)
    {
        if (!idByKey.TryGetValue(key, out LumonScenePatchId id))
        {
            return;
        }

        MarkDirty(id, flags);
    }

    public void MarkAllDirty(LumonScenePatchDirtyFlags flags)
    {
        for (int i = 0; i < records.Count; i++)
        {
            PatchRecord rec = records[i];
            rec.DirtyFlags |= flags;
            records[i] = rec;
        }
    }

    public void ClearDirty(LumonScenePatchId id, LumonScenePatchDirtyFlags flags)
    {
        int idx = id.Value;
        if ((uint)idx >= (uint)records.Count)
        {
            return;
        }

        PatchRecord rec = records[idx];
        rec.DirtyFlags &= ~flags;
        records[idx] = rec;
    }
}
