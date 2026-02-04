using System;

using VanillaGraphicsExpanded.LumOn.WorldCells;
using VanillaGraphicsExpanded.Numerics;

namespace VanillaGraphicsExpanded.LumOn.Scene;

/// <summary>
/// Phase 9.1: schedulable LumonScene region cell (chunk-aligned).
/// Identity is stable by chunk coord key (not chunkSlot).
/// </summary>
internal sealed class LumonSceneRegionCell : WorldCell
{
    private const int HeatHoldFrames = 10;
    private const int ChunkSizeBlocks = LumonSceneVoxelPatchKeyUtil.ChunkSizeVoxels;
    private const int ChunkCenterHalfBlocks = ChunkSizeBlocks - 1;

    public LumonSceneRegionCell(WorldCellKind kind, in LumonSceneChunkCoord chunkCoord)
        : base(CreateKey(kind, in chunkCoord))
    {
        if (kind is not (WorldCellKind.LumonSceneNear or WorldCellKind.LumonSceneFar))
        {
            throw new ArgumentOutOfRangeException(nameof(kind), kind, "LumonSceneRegionCell requires a LumonSceneNear/Far kind.");
        }

        ChunkCoord = chunkCoord;
        ChunkCoordKey = chunkCoord.ToKey();
        ChunkCoordInt3 = new VectorInt3(chunkCoord.X, chunkCoord.Y, chunkCoord.Z);

        // Chunk spans [base..base+31] in block coords. Center is base+15.5, represented as half-block:
        // (base * 2) + 31.
        CenterHalfBlockPos = (ChunkCoordInt3 * (ChunkSizeBlocks * 2)) + ChunkCenterHalfBlocks;

        ChunkSlot = uint.MaxValue;
        SlotGeneration = 0;
        LastSeenInSlotTick = 0;

        // Default: unloaded until slot ownership confirms otherwise.
        DesiredState = WorldCellDesiredState.Unloaded;
        ActualState = WorldCellActualState.Unloaded;
    }

    public LumonSceneChunkCoord ChunkCoord { get; }

    public ulong ChunkCoordKey { get; }

    public VectorInt3 ChunkCoordInt3 { get; }

    public uint ChunkSlot { get; private set; }

    public ushort SlotGeneration { get; private set; }

    public long LastSeenInSlotTick { get; private set; }

    public bool HasAssignedSlot => ChunkSlot != uint.MaxValue;

    public int ResidentPages { get; private set; }

    public int NeedsCapturePages { get; private set; }

    public int CapturingPages { get; private set; }

    public int NeedsRelightPages { get; private set; }

    public int RelightingPages { get; private set; }

    public int ReadyToSamplePages { get; private set; }

    public long HeatUntilTick { get; private set; }

    public int LastHeatRequestCount { get; private set; }

    private int allocFailStreak;
    private long lastAllocFailTick;

    private int budgetStarveStreak;
    private long lastBudgetStarveTick;

    public void UpdateSlotAssignment(uint chunkSlot, ushort slotGeneration, long nowTick)
    {
        ushort prevGen = SlotGeneration;

        ChunkSlot = chunkSlot;
        SlotGeneration = slotGeneration;
        LastSeenInSlotTick = nowTick;

        // Use slotGeneration as a cheap version stamp for "content staleness".
        int genVersion = slotGeneration;
        if (genVersion != CurrentVersion)
        {
            CurrentVersion = genVersion;
        }

        // If the slot was reassigned/recycled, briefly downgrade from Active to Loaded to avoid
        // issuing capture/relight work on a slot that is still settling.
        if (prevGen != 0 && slotGeneration != prevGen && ActualState == WorldCellActualState.Active)
        {
            ActualState = WorldCellActualState.Loaded;
            NextEligibleTick = Math.Max(NextEligibleTick, nowTick + 2);
        }

        // Slot assignment is the minimum requirement for "Loaded".
        if (ActualState == WorldCellActualState.Unloaded)
        {
            ActualState = WorldCellActualState.Loaded;
        }
    }

    public void ClearSlotAssignment(long nowTick)
    {
        ChunkSlot = uint.MaxValue;
        SlotGeneration = 0;
        LastSeenInSlotTick = nowTick;
        ActualState = WorldCellActualState.Unloaded;

        ResidentPages = 0;
        NeedsCapturePages = 0;
        CapturingPages = 0;
        NeedsRelightPages = 0;
        RelightingPages = 0;
        ReadyToSamplePages = 0;

        HeatUntilTick = 0;
        LastHeatRequestCount = 0;
    }

    public void ApplyHeatFromRequests(int requestCount, long nowTick)
    {
        requestCount = Math.Max(0, requestCount);
        LastHeatRequestCount = requestCount;

        if (requestCount <= 0)
        {
            return;
        }

        long until = nowTick + HeatHoldFrames;
        if (until > HeatUntilTick)
        {
            HeatUntilTick = until;
        }
    }

    public bool TryRefreshSlotAssignmentFromFeedback(LumonSceneFeedbackUpdateRenderer feedback, long nowTick)
    {
        if (feedback is null) throw new ArgumentNullException(nameof(feedback));

        if (feedback.TryGetNearChunkSlotAndGeneration(ChunkCoordInt3, out uint slot, out ushort gen))
        {
            UpdateSlotAssignment(slot, gen, nowTick);
            return true;
        }

        ClearSlotAssignment(nowTick);
        return false;
    }

    public bool TryUpdateBacklogFromPageTableMirror(LumonScenePageTableEntry[] pageTableMirror)
    {
        if (pageTableMirror is null) throw new ArgumentNullException(nameof(pageTableMirror));

        if (!HasAssignedSlot)
        {
            ResidentPages = 0;
            NeedsCapturePages = 0;
            CapturingPages = 0;
            NeedsRelightPages = 0;
            RelightingPages = 0;
            ReadyToSamplePages = 0;
            return false;
        }

        int vpc = LumonSceneVirtualAtlasConstants.VirtualPagesPerChunk;
        int baseIndex = checked((int)ChunkSlot * vpc);
        if ((uint)baseIndex >= (uint)pageTableMirror.Length)
        {
            return false;
        }

        int end = Math.Min(pageTableMirror.Length, baseIndex + vpc);

        int resident = 0;
        int needsCapture = 0;
        int capturing = 0;
        int needsRelight = 0;
        int relighting = 0;
        int ready = 0;

        for (int i = baseIndex; i < end; i++)
        {
            var entry = pageTableMirror[i];
            if (LumonScenePageTableEntryPacking.UnpackPhysicalPageId(entry) == 0u) continue;

            var flags = LumonScenePageTableEntryPacking.UnpackFlags(entry);
            if ((flags & LumonScenePageTableEntryPacking.Flags.Resident) != 0) resident++;
            if ((flags & LumonScenePageTableEntryPacking.Flags.NeedsCapture) != 0) needsCapture++;
            if ((flags & LumonScenePageTableEntryPacking.Flags.Capturing) != 0) capturing++;
            if ((flags & LumonScenePageTableEntryPacking.Flags.NeedsRelight) != 0) needsRelight++;
            if ((flags & LumonScenePageTableEntryPacking.Flags.Relighting) != 0) relighting++;
            if (LumonScenePageTableEntryPacking.IsReadyForSampling(entry)) ready++;
        }

        ResidentPages = resident;
        NeedsCapturePages = needsCapture;
        CapturingPages = capturing;
        NeedsRelightPages = needsRelight;
        RelightingPages = relighting;
        ReadyToSamplePages = ready;
        return true;
    }

    public void UpdateBacklogFromSlotStats(in LumonSceneChunkSlotPageTableStats stats)
    {
        ResidentPages = stats.Resident;
        NeedsCapturePages = stats.NeedsCapture;
        CapturingPages = stats.Capturing;
        NeedsRelightPages = stats.NeedsRelight;
        RelightingPages = stats.Relighting;
        ReadyToSamplePages = stats.ReadyToSample;
    }

    public void EnqueueStateAndWork(IWorldCellWorkSink sink, in WorldCellPriorityContext priorityContext)
    {
        if (sink is null) throw new ArgumentNullException(nameof(sink));

        // Always start by removing from work queues; we will re-upsert if eligible.
        sink.Remove(Key, WorldCellWorkQueue.Capture);
        sink.Remove(Key, WorldCellWorkQueue.Relight);

        // Decide whether a lifecycle transition is needed.
        if (WorldCellStateMachine.TryGetNextAction(DesiredState, ActualState, out WorldCellTransitionAction action))
        {
            float transitionPriority = 10000f;
            if (action == WorldCellTransitionAction.EnsureLoaded || action == WorldCellTransitionAction.EnsureActive)
            {
                // Bias closer chunks higher (cheap heuristic).
                float p = CalculatePriority(in priorityContext);
                if (!float.IsFinite(p) || p < 0f) p = 0f;
                transitionPriority += p;
            }

            sink.Upsert(Key, WorldCellWorkQueue.StateTransition, transitionPriority);
        }
        else
        {
            sink.Remove(Key, WorldCellWorkQueue.StateTransition);
        }

        // Only actually-active cells participate in update work.
        if (DesiredState != WorldCellDesiredState.Active || ActualState != WorldCellActualState.Active)
        {
            return;
        }

        if (!HasAssignedSlot)
        {
            return;
        }

        // Phase 9.1: cooldown/backoff. Suppress capture/relight work while in cooldown.
        if (priorityContext.NowTick < NextEligibleTick)
        {
            return;
        }

        float basePri = CalculatePriority(in priorityContext);

        if (NeedsCapturePages > 0)
        {
            sink.Upsert(Key, WorldCellWorkQueue.Capture, basePri + 5000f + NeedsCapturePages);
        }
        else if (LastHeatRequestCount > 0)
        {
            // New allocations won't show up in the page table until after cpuProcessor runs;
            // allow heat to bias which cells' requests get processed first.
            sink.Upsert(Key, WorldCellWorkQueue.Capture, basePri + 1000f + LastHeatRequestCount);
        }

        if (NeedsRelightPages > 0)
        {
            sink.Upsert(Key, WorldCellWorkQueue.Relight, basePri + 2500f + NeedsRelightPages);
        }
    }

    internal void ApplyAllocationFailureBackoff(long nowTick)
    {
        if (nowTick <= 0)
        {
            return;
        }

        if (nowTick - lastAllocFailTick > 1)
        {
            allocFailStreak = 0;
        }

        allocFailStreak = Math.Min(8, allocFailStreak + 1);
        lastAllocFailTick = nowTick;

        // Exponential backoff: 2,4,8,16,32,64... clamped.
        int shift = Math.Min(6, allocFailStreak);
        long cooldown = 1L << shift;
        NextEligibleTick = Math.Max(NextEligibleTick, nowTick + cooldown);
    }

    internal void ApplyBudgetStarvationBackoff(long nowTick, bool isPerSlotBudget)
    {
        if (nowTick <= 0)
        {
            return;
        }

        if (nowTick - lastBudgetStarveTick > 1)
        {
            budgetStarveStreak = 0;
        }

        budgetStarveStreak = Math.Min(16, budgetStarveStreak + 1);
        lastBudgetStarveTick = nowTick;

        long cooldown = isPerSlotBudget
            ? Math.Min(120, 30 + budgetStarveStreak * 5)
            : Math.Min(30, 1 + budgetStarveStreak * 2);

        NextEligibleTick = Math.Max(NextEligibleTick, nowTick + cooldown);
    }

    internal void NotifyAllocationSuccess(long nowTick)
    {
        _ = nowTick;
        allocFailStreak = 0;
        budgetStarveStreak = 0;
    }

    public override WorldCellDesiredState CalculateDesiredState(in WorldCellStateTransitionContext context)
    {
        if (context.HasLoadedWindow)
        {
            bool inLoaded = ChunkCoordInt3.X >= context.LoadedWindowMinRegion.X
                            && ChunkCoordInt3.Y >= context.LoadedWindowMinRegion.Y
                            && ChunkCoordInt3.Z >= context.LoadedWindowMinRegion.Z
                            && ChunkCoordInt3.X <= context.LoadedWindowMaxRegion.X
                            && ChunkCoordInt3.Y <= context.LoadedWindowMaxRegion.Y
                            && ChunkCoordInt3.Z <= context.LoadedWindowMaxRegion.Z;

            if (!inLoaded)
            {
                return WorldCellDesiredState.Unloaded;
            }
        }

        if (context.HasActiveWindow)
        {
            bool inActive = ChunkCoordInt3.X >= context.ActiveWindowMinRegion.X
                            && ChunkCoordInt3.Y >= context.ActiveWindowMinRegion.Y
                            && ChunkCoordInt3.Z >= context.ActiveWindowMinRegion.Z
                            && ChunkCoordInt3.X <= context.ActiveWindowMaxRegion.X
                            && ChunkCoordInt3.Y <= context.ActiveWindowMaxRegion.Y
                            && ChunkCoordInt3.Z <= context.ActiveWindowMaxRegion.Z;

            if (inActive)
            {
                return WorldCellDesiredState.Active;
            }

            // Heat can promote Loaded→Active outside the active window.
            if (HeatUntilTick > context.NowTick)
            {
                return WorldCellDesiredState.Active;
            }

            return WorldCellDesiredState.Loaded;
        }

        // If we have no windows, default to Active.
        return WorldCellDesiredState.Active;
    }

    public override float CalculatePriority(in WorldCellPriorityContext context)
    {
        // v1: keep this minimal; LumonScene scheduling will compute work pressure later.
        // For now, treat non-loaded as ineligible.
        if (!HasAssignedSlot)
        {
            return float.NegativeInfinity;
        }

        if (context.NowTick < NextEligibleTick)
        {
            return float.NegativeInfinity;
        }

        VectorInt3 anchorBlock = context.HasAnchor ? context.AnchorBlockPos : context.CameraBlockPos;
        VectorInt3 anchorChunk = LumonSceneTraceSceneClipmapMath.WorldCellToRegionCoord(anchorBlock);

        int dx = ChunkCoordInt3.X - anchorChunk.X;
        int dy = ChunkCoordInt3.Y - anchorChunk.Y;
        int dz = ChunkCoordInt3.Z - anchorChunk.Z;

        long dist2l = (long)dx * dx + (long)dy * dy + (long)dz * dz;
        float dist2 = dist2l <= int.MaxValue ? dist2l : int.MaxValue;

        // Simple distance falloff (same shape as TraceScene for now).
        return 1000f / (1f + dist2);
    }

    private static WorldCellKey CreateKey(WorldCellKind kind, in LumonSceneChunkCoord coord)
    {
        ulong packed = coord.ToKey();
        return kind == WorldCellKind.LumonSceneNear
            ? WorldCellKey.FromLumonSceneNear(packed)
            : WorldCellKey.FromLumonSceneFar(packed);
    }
}
