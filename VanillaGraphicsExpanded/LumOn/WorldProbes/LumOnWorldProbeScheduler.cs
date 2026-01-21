using System;
using System.Collections.Generic;

using Vintagestory.API.MathTools;

namespace VanillaGraphicsExpanded.LumOn.WorldProbes;

/// <summary>
/// CPU-side scheduler for Phase 18 world-probe clipmap updates.
/// Produces a deterministic per-level list of probes to trace each frame,
/// enforcing per-level and global budgets.
/// </summary>
internal sealed class LumOnWorldProbeScheduler
{
    #region Constants

    private const int DefaultStaleAfterFramesL0 = 600; // ~10s @ 60fps

    // Conservative estimate for CPU->GPU upload per probe update (headers + samples + resolve writes).
    // This will be refined once Phase 18.7 defines concrete SSBO payload sizes.
    private const int EstimatedUploadBytesPerProbe = 64;

    #endregion

    #region Fields

    private readonly LevelState[] levels;

    private readonly int resolution;

    private readonly int probesPerLevel;

    #endregion

    #region Construction

    public LumOnWorldProbeScheduler(int levelCount, int resolution)
    {
        if (levelCount <= 0) throw new ArgumentOutOfRangeException(nameof(levelCount));
        if (resolution <= 0) throw new ArgumentOutOfRangeException(nameof(resolution));

        this.resolution = resolution;
        probesPerLevel = checked(resolution * resolution * resolution);

        levels = new LevelState[levelCount];
        for (int level = 0; level < levels.Length; level++)
        {
            levels[level] = new LevelState(resolution, probesPerLevel);
        }
    }

    #endregion

    #region Public API

    public readonly record struct WorldProbeAnchorShiftEvent(
        int Level,
        Vec3d PrevAnchor,
        Vec3d NewAnchor,
        Vec3i DeltaProbes,
        double Spacing,
        Vec3d PrevOriginMinCorner,
        Vec3d NewOriginMinCorner,
        Vec3i PrevRingOffset,
        Vec3i NewRingOffset);

    /// <summary>
    /// Fired whenever a level's snapped clipmap anchor shifts (and the ring offset/origin updates).
    /// Not fired for first-time initialization.
    /// </summary>
    public event Action<WorldProbeAnchorShiftEvent>? AnchorShifted;

    public int LevelCount => levels.Length;

    public int Resolution => resolution;

    public int ProbesPerLevel => probesPerLevel;

    public bool TryCopyLifecycleStates(int level, LumOnWorldProbeLifecycleState[] destination)
    {
        if ((uint)level >= (uint)levels.Length)
        {
            return false;
        }

        if (destination is null) throw new ArgumentNullException(nameof(destination));
        if (destination.Length < probesPerLevel)
        {
            throw new ArgumentException($"Destination must be at least {probesPerLevel} elements.", nameof(destination));
        }

        levels[level].CopyLifecycleTo(destination);
        return true;
    }

    public bool TryGetLevelParams(int level, out Vec3d originMinCorner, out Vec3i ringOffset)
    {
        if ((uint)level >= (uint)levels.Length)
        {
            originMinCorner = new Vec3d();
            ringOffset = new Vec3i();
            return false;
        }

        ref LevelState state = ref levels[level];
        return state.TryGetParams(out originMinCorner, out ringOffset);
    }

    public void ResetAll()
    {
        foreach (ref LevelState level in levels.AsSpan())
        {
            level.Reset();
        }
    }

    /// <summary>
    /// Update per-level origins (snapped) and mark newly introduced probe slabs dirty.
    /// </summary>
    public void UpdateOrigins(Vec3d cameraPos, double baseSpacing)
    {
        ArgumentNullException.ThrowIfNull(cameraPos);
        if (baseSpacing <= 0) throw new ArgumentOutOfRangeException(nameof(baseSpacing));

        for (int level = 0; level < levels.Length; level++)
        {
            ref LevelState state = ref levels[level];
            double spacing = LumOnClipmapTopology.GetSpacing(baseSpacing, level);
            if (!state.UpdateOrigin(cameraPos, spacing, out var shiftInfo))
            {
                continue;
            }

            AnchorShifted?.Invoke(new WorldProbeAnchorShiftEvent(
                Level: level,
                PrevAnchor: shiftInfo.PrevAnchor,
                NewAnchor: shiftInfo.NewAnchor,
                DeltaProbes: shiftInfo.DeltaProbes,
                Spacing: spacing,
                PrevOriginMinCorner: shiftInfo.PrevOriginMinCorner,
                NewOriginMinCorner: shiftInfo.NewOriginMinCorner,
                PrevRingOffset: shiftInfo.PrevRingOffset,
                NewRingOffset: shiftInfo.NewRingOffset));
        }
    }

    /// <summary>
    /// Build a deterministic probe update list, enforcing global and per-level budgets.
    /// </summary>
    public List<LumOnWorldProbeUpdateRequest> BuildUpdateList(
        int frameIndex,
        Vec3d cameraPos,
        double baseSpacing,
        int[] perLevelProbeBudgets,
        int traceMaxProbesPerFrame,
        int uploadBudgetBytesPerFrame)
    {
        ArgumentNullException.ThrowIfNull(cameraPos);
        if (baseSpacing <= 0) throw new ArgumentOutOfRangeException(nameof(baseSpacing));
        ArgumentNullException.ThrowIfNull(perLevelProbeBudgets);

        int globalCpuRemaining = Math.Max(0, traceMaxProbesPerFrame);
        int globalUploadRemainingBytes = Math.Max(0, uploadBudgetBytesPerFrame);

        int globalUploadRemainingProbes = EstimatedUploadBytesPerProbe <= 0
            ? globalCpuRemaining
            : globalUploadRemainingBytes / EstimatedUploadBytesPerProbe;

        int globalRemaining = Math.Min(globalCpuRemaining, globalUploadRemainingProbes);

        var list = new List<LumOnWorldProbeUpdateRequest>(capacity: Math.Min(globalRemaining, 1024));

        for (int level = 0; level < levels.Length && globalRemaining > 0; level++)
        {
            int budgetL = level < perLevelProbeBudgets.Length ? perLevelProbeBudgets[level] : 0;
            budgetL = Math.Max(0, budgetL);

            int take = Math.Min(budgetL, globalRemaining);
            if (take <= 0) continue;

            ref LevelState state = ref levels[level];
            double spacing = LumOnClipmapTopology.GetSpacing(baseSpacing, level);
            state.Select(level, frameIndex, cameraPos, spacing, take, list, out int taken);

            globalRemaining -= taken;
        }

        return list;
    }

    /// <summary>
    /// Mark a probe update request as completed.
    /// This is a Phase 18.5 hook; Phase 18.6+ will drive this from the async trace backend.
    /// </summary>
    public void Complete(in LumOnWorldProbeUpdateRequest request, int frameIndex, bool success)
    {
        if ((uint)request.Level >= (uint)levels.Length) throw new ArgumentOutOfRangeException(nameof(request));

        ref LevelState state = ref levels[request.Level];
        state.Complete(request.StorageLinearIndex, frameIndex, success);
    }

    /// <summary>
    /// Optional hook for block/chunk changes: mark all probes whose centers fall inside the world AABB dirty.
    /// </summary>
    public void MarkDirtyWorldAabb(int level, Vec3d minWorld, Vec3d maxWorld, double baseSpacing)
    {
        if ((uint)level >= (uint)levels.Length) throw new ArgumentOutOfRangeException(nameof(level));
        ArgumentNullException.ThrowIfNull(minWorld);
        ArgumentNullException.ThrowIfNull(maxWorld);
        if (baseSpacing <= 0) throw new ArgumentOutOfRangeException(nameof(baseSpacing));

        ref LevelState state = ref levels[level];
        double spacing = LumOnClipmapTopology.GetSpacing(baseSpacing, level);
        state.MarkDirtyWorldAabb(minWorld, maxWorld, spacing);
    }

    #endregion

    #region Level State

    private struct LevelState
    {
        private readonly int resolution;
        private readonly int probesPerLevel;

        private readonly LumOnWorldProbeLifecycleState[] lifecycle;
        private readonly int[] lastUpdatedFrame;
        private readonly bool[] dirtyAfterInFlight;

        private Vec3d? anchor;
        private Vec3d? originMinCorner;

        // Maps a local index to the storage coordinate where that probe currently lives.
        // Updated on origin shifts to preserve existing probe data.
        private Vec3i ringOffset;

        public LevelState(int resolution, int probesPerLevel)
        {
            this.resolution = resolution;
            this.probesPerLevel = probesPerLevel;

            lifecycle = new LumOnWorldProbeLifecycleState[probesPerLevel];
            lastUpdatedFrame = new int[probesPerLevel];
            dirtyAfterInFlight = new bool[probesPerLevel];

            anchor = null;
            originMinCorner = null;
            ringOffset = new Vec3i(0, 0, 0);
        }

        public void Reset()
        {
            Array.Fill(lifecycle, LumOnWorldProbeLifecycleState.Uninitialized);
            Array.Fill(lastUpdatedFrame, 0);
            Array.Fill(dirtyAfterInFlight, false);
            anchor = null;
            originMinCorner = null;
            ringOffset = new Vec3i(0, 0, 0);
        }

        public void CopyLifecycleTo(LumOnWorldProbeLifecycleState[] destination)
        {
            Array.Copy(lifecycle, destination, lifecycle.Length);
        }

        public bool TryGetParams(out Vec3d origin, out Vec3i ring)
        {
            if (originMinCorner is null)
            {
                origin = new Vec3d();
                ring = new Vec3i();
                return false;
            }

            origin = originMinCorner!;
            ring = ringOffset;
            return true;
        }

        public readonly record struct AnchorShiftInfo(
            Vec3d PrevAnchor,
            Vec3d NewAnchor,
            Vec3i DeltaProbes,
            Vec3d PrevOriginMinCorner,
            Vec3d NewOriginMinCorner,
            Vec3i PrevRingOffset,
            Vec3i NewRingOffset);

        public bool UpdateOrigin(Vec3d cameraPos, double spacing, out AnchorShiftInfo shiftInfo)
        {
            Vec3d newAnchor = LumOnClipmapTopology.SnapAnchor(cameraPos, spacing);

            if (anchor is null)
            {
                anchor = newAnchor;
                originMinCorner = LumOnClipmapTopology.GetOriginMinCorner(newAnchor, spacing, resolution);

                // First-time init: everything is dirty/uninitialized.
                Array.Fill(lifecycle, LumOnWorldProbeLifecycleState.Uninitialized);
                shiftInfo = default;
                return false;
            }

            Vec3d prevAnchor = anchor!;

            int dx = (int)Math.Round((newAnchor.X - prevAnchor.X) / spacing);
            int dy = (int)Math.Round((newAnchor.Y - prevAnchor.Y) / spacing);
            int dz = (int)Math.Round((newAnchor.Z - prevAnchor.Z) / spacing);

            if (dx == 0 && dy == 0 && dz == 0)
            {
                // No origin shift.
                shiftInfo = default;
                return false;
            }

            Vec3d prevOrigin = originMinCorner ?? new Vec3d();
            Vec3i prevRing = ringOffset;

            // Update anchor/origin.
            anchor = newAnchor;
            Vec3d newOrigin = LumOnClipmapTopology.GetOriginMinCorner(newAnchor, spacing, resolution);
            originMinCorner = newOrigin;

            // Preserve overlapping region by shifting the ring offset.
            Vec3i newRing = LumOnClipmapTopology.WrapIndex(
                new Vec3i(ringOffset.X + dx, ringOffset.Y + dy, ringOffset.Z + dz),
                resolution);
            ringOffset = newRing;

            // Mark newly introduced slabs dirty.
            MarkIntroducedSlabsDirty(dx, dy, dz);

            shiftInfo = new AnchorShiftInfo(
                PrevAnchor: new Vec3d(prevAnchor.X, prevAnchor.Y, prevAnchor.Z),
                NewAnchor: new Vec3d(newAnchor.X, newAnchor.Y, newAnchor.Z),
                DeltaProbes: new Vec3i(dx, dy, dz),
                PrevOriginMinCorner: new Vec3d(prevOrigin.X, prevOrigin.Y, prevOrigin.Z),
                NewOriginMinCorner: new Vec3d(newOrigin.X, newOrigin.Y, newOrigin.Z),
                PrevRingOffset: new Vec3i(prevRing.X, prevRing.Y, prevRing.Z),
                NewRingOffset: new Vec3i(newRing.X, newRing.Y, newRing.Z));
            return true;
        }

        public void Select(
            int level,
            int frameIndex,
            Vec3d cameraPos,
            double spacing,
            int budget,
            List<LumOnWorldProbeUpdateRequest> output,
            out int taken)
        {
            taken = 0;
            if (budget <= 0) return;

            if (level < 0) throw new ArgumentOutOfRangeException(nameof(level));

            if (originMinCorner is null)
            {
                // Origins not initialized yet; treat as all uninitialized.
                originMinCorner = LumOnClipmapTopology.GetOriginMinCorner(
                    LumOnClipmapTopology.SnapAnchor(cameraPos, spacing),
                    spacing,
                    resolution);
            }

            Vec3d origin = originMinCorner!;

            // Promote valid probes to stale if their age exceeds the per-level threshold.
            int staleAfter = GetStaleAfterFrames(spacing);
            for (int i = 0; i < lifecycle.Length; i++)
            {
                if (lifecycle[i] != LumOnWorldProbeLifecycleState.Valid) continue;
                int age = frameIndex - lastUpdatedFrame[i];
                if (age >= staleAfter)
                {
                    lifecycle[i] = LumOnWorldProbeLifecycleState.Stale;
                }
            }

            Vec3d localCam = LumOnClipmapTopology.WorldToLocal(cameraPos, origin, spacing);
            Vec3i camIndex = LumOnClipmapTopology.LocalToIndexFloor(localCam);

            // Deterministic selection:
            //  1) state priority (Dirty/Uninitialized, then Stale)
            //  2) distance to camera index (squared)
            //  3) linear local index (stable tiebreak)
            //
            // This is O(N) scan over the level grid; resolution defaults are small enough for Phase 18 bring-up.
            Candidate[] bestArr = new Candidate[budget];
            Span<Candidate> best = bestArr;
            int bestCount = 0;

            for (int z = 0; z < resolution; z++)
            {
                for (int y = 0; y < resolution; y++)
                {
                    for (int x = 0; x < resolution; x++)
                    {
                        var local = new Vec3i(x, y, z);
                        int localLinear = x + y * resolution + z * resolution * resolution;

                        Vec3i storage = LocalToStorage(local);
                        int storageLinear = LumOnClipmapTopology.LinearIndex(storage, resolution);

                        LumOnWorldProbeLifecycleState s = lifecycle[storageLinear];
                        if (s is LumOnWorldProbeLifecycleState.InFlight or LumOnWorldProbeLifecycleState.Valid)
                        {
                            continue;
                        }

                        int statePri = s switch
                        {
                            LumOnWorldProbeLifecycleState.Dirty => 0,
                            LumOnWorldProbeLifecycleState.Uninitialized => 0,
                            LumOnWorldProbeLifecycleState.Stale => 1,
                            _ => 2,
                        };

                        int dx = x - camIndex.X;
                        int dy = y - camIndex.Y;
                        int dz = z - camIndex.Z;
                        int dist2 = (dx * dx) + (dy * dy) + (dz * dz);

                        var cand = new Candidate(statePri, dist2, localLinear, local, storage, storageLinear);
                        InsertBest(best, ref bestCount, cand);
                    }
                }
            }

            // Emit in priority order (best[] is maintained sorted).
            for (int i = 0; i < bestCount; i++)
            {
                Candidate c = best[i];

                lifecycle[c.StorageLinearIndex] = LumOnWorldProbeLifecycleState.InFlight;
                output.Add(new LumOnWorldProbeUpdateRequest(
                    Level: level,
                    LocalIndex: c.LocalIndex,
                    StorageIndex: c.StorageIndex,
                    StorageLinearIndex: c.StorageLinearIndex));

                taken++;
            }
        }

        public void Complete(int storageLinearIndex, int frameIndex, bool success)
        {
            if ((uint)storageLinearIndex >= (uint)probesPerLevel) throw new ArgumentOutOfRangeException(nameof(storageLinearIndex));

            if (dirtyAfterInFlight[storageLinearIndex])
            {
                dirtyAfterInFlight[storageLinearIndex] = false;
                lifecycle[storageLinearIndex] = LumOnWorldProbeLifecycleState.Dirty;
                return;
            }

            lifecycle[storageLinearIndex] = success ? LumOnWorldProbeLifecycleState.Valid : LumOnWorldProbeLifecycleState.Dirty;
            if (success)
            {
                lastUpdatedFrame[storageLinearIndex] = frameIndex;
            }
        }

        public void MarkDirtyWorldAabb(Vec3d minWorld, Vec3d maxWorld, double spacing)
        {
            if (originMinCorner is null) return;

            Vec3d origin = originMinCorner!;

            Vec3d lo = new(Math.Min(minWorld.X, maxWorld.X), Math.Min(minWorld.Y, maxWorld.Y), Math.Min(minWorld.Z, maxWorld.Z));
            Vec3d hi = new(Math.Max(minWorld.X, maxWorld.X), Math.Max(minWorld.Y, maxWorld.Y), Math.Max(minWorld.Z, maxWorld.Z));

            Vec3d localLo = LumOnClipmapTopology.WorldToLocal(lo, origin, spacing);
            Vec3d localHi = LumOnClipmapTopology.WorldToLocal(hi, origin, spacing);

            int x0 = Math.Clamp((int)Math.Floor(localLo.X), 0, resolution - 1);
            int y0 = Math.Clamp((int)Math.Floor(localLo.Y), 0, resolution - 1);
            int z0 = Math.Clamp((int)Math.Floor(localLo.Z), 0, resolution - 1);

            int x1 = Math.Clamp((int)Math.Floor(localHi.X), 0, resolution - 1);
            int y1 = Math.Clamp((int)Math.Floor(localHi.Y), 0, resolution - 1);
            int z1 = Math.Clamp((int)Math.Floor(localHi.Z), 0, resolution - 1);

            for (int z = z0; z <= z1; z++)
            {
                for (int y = y0; y <= y1; y++)
                {
                    for (int x = x0; x <= x1; x++)
                    {
                        Vec3i storage = LocalToStorage(new Vec3i(x, y, z));
                        int storageLinear = LumOnClipmapTopology.LinearIndex(storage, resolution);

                        if (lifecycle[storageLinear] != LumOnWorldProbeLifecycleState.InFlight)
                        {
                            lifecycle[storageLinear] = LumOnWorldProbeLifecycleState.Dirty;
                        }
                        else
                        {
                            dirtyAfterInFlight[storageLinear] = true;
                        }
                    }
                }
            }
        }

        private int GetStaleAfterFrames(double spacing)
        {
            // Coarser levels update less frequently.
            // Using spacing ratio is stable and avoids needing explicit config for staleness.
            double ratio = Math.Max(1.0, spacing);
            double scaled = DefaultStaleAfterFramesL0 * ratio;
            return (int)Math.Clamp(scaled, 60.0, 60_000.0);
        }

        private Vec3i LocalToStorage(Vec3i local)
        {
            return LumOnClipmapTopology.WrapIndex(new Vec3i(local.X + ringOffset.X, local.Y + ringOffset.Y, local.Z + ringOffset.Z), resolution);
        }

        private void MarkIntroducedSlabsDirty(int dx, int dy, int dz)
        {
            // Clamp to resolution (large teleports invalidate everything).
            int ax = Math.Min(Math.Abs(dx), resolution);
            int ay = Math.Min(Math.Abs(dy), resolution);
            int az = Math.Min(Math.Abs(dz), resolution);

            if (ax == resolution || ay == resolution || az == resolution)
            {
                Array.Fill(lifecycle, LumOnWorldProbeLifecycleState.Dirty);
                return;
            }

            if (ax > 0)
            {
                int xStart = dx > 0 ? resolution - ax : 0;
                int xEnd = dx > 0 ? resolution - 1 : ax - 1;
                MarkSlabDirty(xStart, xEnd, 0, resolution - 1, 0, resolution - 1);
            }

            if (ay > 0)
            {
                int yStart = dy > 0 ? resolution - ay : 0;
                int yEnd = dy > 0 ? resolution - 1 : ay - 1;
                MarkSlabDirty(0, resolution - 1, yStart, yEnd, 0, resolution - 1);
            }

            if (az > 0)
            {
                int zStart = dz > 0 ? resolution - az : 0;
                int zEnd = dz > 0 ? resolution - 1 : az - 1;
                MarkSlabDirty(0, resolution - 1, 0, resolution - 1, zStart, zEnd);
            }
        }

        private void MarkSlabDirty(int x0, int x1, int y0, int y1, int z0, int z1)
        {
            for (int z = z0; z <= z1; z++)
            {
                for (int y = y0; y <= y1; y++)
                {
                    for (int x = x0; x <= x1; x++)
                    {
                        Vec3i storage = LocalToStorage(new Vec3i(x, y, z));
                        int storageLinear = LumOnClipmapTopology.LinearIndex(storage, resolution);

                        if (lifecycle[storageLinear] != LumOnWorldProbeLifecycleState.InFlight)
                        {
                            lifecycle[storageLinear] = LumOnWorldProbeLifecycleState.Dirty;
                        }
                        else
                        {
                            dirtyAfterInFlight[storageLinear] = true;
                        }
                    }
                }
            }
        }

        private readonly struct Candidate
        {
            public readonly int StatePriority;
            public readonly int Dist2;
            public readonly int LocalLinearIndex;
            public readonly Vec3i LocalIndex;
            public readonly Vec3i StorageIndex;
            public readonly int StorageLinearIndex;

            public Candidate(int statePriority, int dist2, int localLinearIndex, Vec3i localIndex, Vec3i storageIndex, int storageLinearIndex)
            {
                StatePriority = statePriority;
                Dist2 = dist2;
                LocalLinearIndex = localLinearIndex;
                LocalIndex = localIndex;
                StorageIndex = storageIndex;
                StorageLinearIndex = storageLinearIndex;
            }
        }

        private static void InsertBest(Span<Candidate> best, ref int count, Candidate cand)
        {
            int insertPos = 0;
            while (insertPos < count && Compare(best[insertPos], cand) <= 0)
            {
                insertPos++;
            }

            if (insertPos >= best.Length)
            {
                return;
            }

            if (count < best.Length)
            {
                for (int i = count; i > insertPos; i--)
                {
                    best[i] = best[i - 1];
                }

                best[insertPos] = cand;
                count++;
                return;
            }

            // Full: shift down and drop last.
            for (int i = best.Length - 1; i > insertPos; i--)
            {
                best[i] = best[i - 1];
            }

            best[insertPos] = cand;
        }

        private static int Compare(in Candidate a, in Candidate b)
        {
            int c = a.StatePriority.CompareTo(b.StatePriority);
            if (c != 0) return c;

            c = a.Dist2.CompareTo(b.Dist2);
            if (c != 0) return c;

            return a.LocalLinearIndex.CompareTo(b.LocalLinearIndex);
        }
    }

    #endregion
}
