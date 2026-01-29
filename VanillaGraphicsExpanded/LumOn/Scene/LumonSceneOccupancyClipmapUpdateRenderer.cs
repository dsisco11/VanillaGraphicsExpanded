using System;
using System.Buffers;
using System.Collections.Generic;
using System.Threading;

using VanillaGraphicsExpanded.Numerics;
using VanillaGraphicsExpanded.Rendering;

using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;

namespace VanillaGraphicsExpanded.LumOn.Scene;

/// <summary>
/// Phase 22.8: Trace scene v1 - occupancy clipmap (CPU build + GPU upload).
/// Stores packed R32UI payload per cell (block/sun/light/material indirection).
/// </summary>
internal sealed class LumonSceneOccupancyClipmapUpdateRenderer : IRenderer, IDisposable
{
    private const double RenderOrderValue = 0.99975;
    private const int RenderRangeValue = 1;

    private readonly ICoreClientAPI capi;
    private readonly VgeConfig config;

    private LumonSceneOccupancyClipmapGpuResources? resources;

    private int lastConfigHash;

    private readonly Dictionary<int, byte> lightKeyToId = new();
    private readonly float[] lightLutData = new float[LumonSceneOccupancyClipmapGpuResources.MaxLightColors * 4];
    private bool lightLutDirty;

    private LevelState[] levelStates = Array.Empty<LevelState>();

    private int rebuildAllRequested;

    public double RenderOrder => RenderOrderValue;
    public int RenderRange => RenderRangeValue;

    public LumonSceneOccupancyClipmapGpuResources? Resources => resources;

    internal bool TryGetLevel0RuntimeParams(out VectorInt3 originMinCell, out VectorInt3 ring, out int resolution)
    {
        originMinCell = default;
        ring = default;
        resolution = 0;

        if (resources is null || levelStates.Length <= 0)
        {
            return false;
        }

        var ls = levelStates[0];
        if (!ls.HasAnchor)
        {
            return false;
        }

        originMinCell = ls.OriginMinCell;
        ring = ls.Ring;
        resolution = ls.Resolution;
        return true;
    }

    private readonly struct SliceWork
    {
        public readonly int Level;
        public readonly Axis Axis;
        public readonly int LocalIndex;

        public SliceWork(int level, Axis axis, int localIndex)
        {
            Level = level;
            Axis = axis;
            LocalIndex = localIndex;
        }
    }

    private enum Axis : byte
    {
        X = 0,
        Y = 1,
        Z = 2
    }

    private sealed class LevelState
    {
        public required int Level;
        public required int Resolution;
        public required int SpacingBlocks;

        public VectorInt3 AnchorCell;
        public VectorInt3 OriginMinCell;
        public VectorInt3 Ring;
        public bool HasAnchor;

        public readonly Queue<SliceWork> Pending = new();

        public int MaintenanceCursorZ;
    }

    public LumonSceneOccupancyClipmapUpdateRenderer(ICoreClientAPI capi, VgeConfig config)
    {
        this.capi = capi ?? throw new ArgumentNullException(nameof(capi));
        this.config = config ?? throw new ArgumentNullException(nameof(config));

        capi.Event.RegisterRenderer(this, EnumRenderStage.Done, "vge_lumonscene_occupancy_clipmap");
        capi.Event.LeaveWorld += OnLeaveWorld;
    }

    public void NotifyAllDirty(string reason)
    {
        _ = reason;
        Interlocked.Exchange(ref rebuildAllRequested, 1);
    }

    public void OnRenderFrame(float deltaTime, EnumRenderStage stage)
    {
        if (stage != EnumRenderStage.Done)
        {
            return;
        }

        if (!config.LumOn.Enabled || !config.LumOn.LumonScene.Enabled)
        {
            return;
        }

        if (!TryGetCameraPosBlock(out int camX, out int camY, out int camZ))
        {
            return;
        }

        EnsureResourcesAndConfig();
        if (resources is null)
        {
            return;
        }

        int budgetSlices = Math.Max(0, config.LumOn.LumonScene.TraceSceneClipmapSlicesPerFrame);

        // Apply rebuild request.
        if (Interlocked.Exchange(ref rebuildAllRequested, 0) != 0)
        {
            EnqueueFullRebuild();
        }

        // Update anchor + enqueue exposed slabs.
        for (int i = 0; i < levelStates.Length; i++)
        {
            UpdateAnchorAndQueueExposedSlabs(levelStates[i], camX, camY, camZ);
        }

        // Drain pending updates under budget.
        int remaining = budgetSlices;
        while (remaining > 0 && TryDequeueWork(out SliceWork work))
        {
            UpdateSlice(work);
            remaining--;
        }

        // Background maintenance: keep the clipmap fresh even without movement.
        if (remaining > 0)
        {
            int perLevel = Math.Max(1, remaining / Math.Max(1, levelStates.Length));
            foreach (var ls in levelStates)
            {
                for (int i = 0; i < perLevel && remaining > 0; i++)
                {
                    int localZ = ls.MaintenanceCursorZ++ % ls.Resolution;
                    ls.Pending.Enqueue(new SliceWork(ls.Level, Axis.Z, localZ));
                    remaining--;
                }
            }
        }

        if (lightLutDirty)
        {
            lightLutDirty = false;
            resources.LightColorLut.UploadDataImmediate(lightLutData);
        }
    }

    public void Dispose()
    {
        capi.Event.LeaveWorld -= OnLeaveWorld;
        resources?.Dispose();
        resources = null;
    }

    private void OnLeaveWorld()
    {
        lastConfigHash = 0;

        resources?.Dispose();
        resources = null;

        levelStates = Array.Empty<LevelState>();

        lightKeyToId.Clear();
        Array.Clear(lightLutData);
        lightLutDirty = false;

        rebuildAllRequested = 0;
    }

    private bool TryGetCameraPosBlock(out int x, out int y, out int z)
    {
        var player = capi.World?.Player;
        if (player?.Entity is null)
        {
            x = y = z = 0;
            return false;
        }

        double px = player.Entity.CameraPos.X;
        double py = player.Entity.CameraPos.Y;
        double pz = player.Entity.CameraPos.Z;
        x = (int)Math.Floor(px);
        y = (int)Math.Floor(py);
        z = (int)Math.Floor(pz);
        return true;
    }

    private void EnsureResourcesAndConfig()
    {
        var cfg = config.LumOn.LumonScene;
        int resolution = cfg.TraceSceneClipmapResolution;
        int levels = cfg.TraceSceneClipmapLevels;

        int configHash = HashCode.Combine(resolution, levels);
        if (resources is not null && configHash == lastConfigHash)
        {
            return;
        }

        lastConfigHash = configHash;

        resources?.Dispose();
        resources = null;

        resolution = Math.Clamp(resolution, 8, 256);
        levels = Math.Clamp(levels, 1, 8);

        resources = new LumonSceneOccupancyClipmapGpuResources(
            resolution: resolution,
            levels: levels,
            debugNamePrefix: "LumOn.LumonScene.TraceScene");

        levelStates = new LevelState[levels];
        for (int i = 0; i < levels; i++)
        {
            levelStates[i] = new LevelState
            {
                Level = i,
                Resolution = resolution,
                SpacingBlocks = 1 << i,
                MaintenanceCursorZ = 0
            };
        }

        lightKeyToId.Clear();
        InitializeLightLutDefaults();

        EnqueueFullRebuild();
    }

    private void InitializeLightLutDefaults()
    {
        // id 0: neutral white. Fill all entries to reduce undefined sampling if ids are used before assignment.
        for (int i = 0; i < LumonSceneOccupancyClipmapGpuResources.MaxLightColors; i++)
        {
            int o = i * 4;
            lightLutData[o + 0] = 1.0f;
            lightLutData[o + 1] = 1.0f;
            lightLutData[o + 2] = 1.0f;
            lightLutData[o + 3] = 1.0f;
        }

        lightLutDirty = true;
    }

    private void EnqueueFullRebuild()
    {
        foreach (var ls in levelStates)
        {
            ls.Pending.Clear();
            for (int z = 0; z < ls.Resolution; z++)
            {
                ls.Pending.Enqueue(new SliceWork(ls.Level, Axis.Z, z));
            }
        }
    }

    private bool TryDequeueWork(out SliceWork work)
    {
        // Simple fair-ish scheduling: walk levels and take from the first non-empty queue.
        for (int i = 0; i < levelStates.Length; i++)
        {
            var q = levelStates[i].Pending;
            if (q.Count > 0)
            {
                work = q.Dequeue();
                return true;
            }
        }

        work = default;
        return false;
    }

    private void UpdateAnchorAndQueueExposedSlabs(LevelState ls, int camX, int camY, int camZ)
    {
        int level = ls.Level;
        int spacing = ls.SpacingBlocks;
        int res = ls.Resolution;
        int half = res / 2;

        // Snap anchor in "cell units" using arithmetic shift (floor division for power-of-two).
        VectorInt3 newAnchorCell = new(camX >> level, camY >> level, camZ >> level);

        if (!ls.HasAnchor)
        {
            ls.AnchorCell = newAnchorCell;
            ls.OriginMinCell = new VectorInt3(newAnchorCell.X - half, newAnchorCell.Y - half, newAnchorCell.Z - half);
            ls.Ring = VectorInt3.Zero;
            ls.HasAnchor = true;
            return;
        }

        int deltaX = newAnchorCell.X - ls.AnchorCell.X;
        int deltaY = newAnchorCell.Y - ls.AnchorCell.Y;
        int deltaZ = newAnchorCell.Z - ls.AnchorCell.Z;
        if (deltaX == 0 && deltaY == 0 && deltaZ == 0)
        {
            return;
        }

        ls.AnchorCell = newAnchorCell;
        ls.OriginMinCell = new VectorInt3(newAnchorCell.X - half, newAnchorCell.Y - half, newAnchorCell.Z - half);

        // Ring-buffer shift: advance ring offsets by delta (mod resolution).
        ls.Ring = new VectorInt3(
            Wrap(ls.Ring.X + deltaX, res),
            Wrap(ls.Ring.Y + deltaY, res),
            Wrap(ls.Ring.Z + deltaZ, res));

        // If the camera jumped by >= resolution cells, easiest is to rebuild the whole level.
        if (Math.Abs(deltaX) >= res || Math.Abs(deltaY) >= res || Math.Abs(deltaZ) >= res)
        {
            for (int z = 0; z < res; z++)
            {
                ls.Pending.Enqueue(new SliceWork(level, Axis.Z, z));
            }
            return;
        }

        EnqueueExposedSlabSlices(ls, Axis.X, deltaX);
        EnqueueExposedSlabSlices(ls, Axis.Y, deltaY);
        EnqueueExposedSlabSlices(ls, Axis.Z, deltaZ);
    }

    private static void EnqueueExposedSlabSlices(LevelState ls, Axis axis, int deltaCells)
    {
        if (deltaCells == 0)
        {
            return;
        }

        int res = ls.Resolution;
        int abs = Math.Min(Math.Abs(deltaCells), res);

        if (deltaCells > 0)
        {
            for (int i = res - abs; i < res; i++)
            {
                ls.Pending.Enqueue(new SliceWork(ls.Level, axis, i));
            }
        }
        else
        {
            for (int i = 0; i < abs; i++)
            {
                ls.Pending.Enqueue(new SliceWork(ls.Level, axis, i));
            }
        }
    }

    private void UpdateSlice(in SliceWork work)
    {
        if (resources is null)
        {
            return;
        }

        LevelState ls = levelStates[work.Level];
        int res = ls.Resolution;
        int level = ls.Level;
        int spacing = ls.SpacingBlocks;

        // Fixed "local" coordinate for the chosen slice.
        int localFixed = Wrap(work.LocalIndex, res);

        int texFixed = work.Axis switch
        {
            Axis.X => Wrap(localFixed + ls.Ring.X, res),
            Axis.Y => Wrap(localFixed + ls.Ring.Y, res),
            _ => Wrap(localFixed + ls.Ring.Z, res)
        };

        // We upload the slice as a contiguous region in texture coordinates.
        // For varying axes, iterate tex coords 0..res-1 and invert the ring mapping back to local coords.
        int expected = checked(res * res);
        uint[] data = ArrayPool<uint>.Shared.Rent(expected);
        try
        {
            FillSliceData(ls, work.Axis, localFixed, data);

            var tex = resources.OccupancyLevels[level];

            switch (work.Axis)
            {
                case Axis.X:
                    tex.UploadDataImmediate(data, x: texFixed, y: 0, z: 0, regionWidth: 1, regionHeight: res, regionDepth: res, mipLevel: 0);
                    break;
                case Axis.Y:
                    tex.UploadDataImmediate(data, x: 0, y: texFixed, z: 0, regionWidth: res, regionHeight: 1, regionDepth: res, mipLevel: 0);
                    break;
                default:
                    tex.UploadDataImmediate(data, x: 0, y: 0, z: texFixed, regionWidth: res, regionHeight: res, regionDepth: 1, mipLevel: 0);
                    break;
            }
        }
        finally
        {
            ArrayPool<uint>.Shared.Return(data, clearArray: false);
        }
    }

    private void FillSliceData(LevelState ls, Axis axis, int localFixed, uint[] outData)
    {
        int res = ls.Resolution;
        int spacing = ls.SpacingBlocks;

        var blockAccessor = capi.World.BlockAccessor;
        int maxY = blockAccessor.MapSizeY;
        var pos = new BlockPos(0);

        // Inverse ring offsets for tex->local conversion.
        int ringX = ls.Ring.X;
        int ringY = ls.Ring.Y;
        int ringZ = ls.Ring.Z;

        int idx = 0;

        for (int texB = 0; texB < res; texB++)
        {
            for (int texA = 0; texA < res; texA++)
            {
                int texX;
                int texY;
                int texZ;

                int localX;
                int localY;
                int localZ;

                // We iterate in texture coordinates and invert to local coords (because the slice region is contiguous in tex-space).
                switch (axis)
                {
                    case Axis.X:
                        texY = texA;
                        texZ = texB;
                        localY = Wrap(texY - ringY, res);
                        localZ = Wrap(texZ - ringZ, res);
                        localX = localFixed;
                        break;

                    case Axis.Y:
                        texX = texA;
                        texZ = texB;
                        localX = Wrap(texX - ringX, res);
                        localZ = Wrap(texZ - ringZ, res);
                        localY = localFixed;
                        break;

                    default:
                        texX = texA;
                        texY = texB;
                        localX = Wrap(texX - ringX, res);
                        localY = Wrap(texY - ringY, res);
                        localZ = localFixed;
                        break;
                }

                int cellX = ls.OriginMinCell.X + localX;
                int cellY = ls.OriginMinCell.Y + localY;
                int cellZ = ls.OriginMinCell.Z + localZ;

                // Sample at cell center in block coords (coarse levels sample one representative voxel).
                int sampleX = cellX * spacing + (spacing >> 1);
                int sampleY = cellY * spacing + (spacing >> 1);
                int sampleZ = cellZ * spacing + (spacing >> 1);

                if ((uint)sampleY >= (uint)maxY)
                {
                    outData[idx++] = 0u;
                    continue;
                }

                pos.Set(sampleX, sampleY, sampleZ);
                Block block = blockAccessor.GetBlock(pos);
                if (block is null)
                {
                    outData[idx++] = 0u;
                    continue;
                }

                // v1 occupancy: treat blocks without collision boxes as empty (air/foliage/etc).
                bool occupied = block.CollisionBoxes is not null && block.CollisionBoxes.Length > 0;
                if (!occupied)
                {
                    outData[idx++] = 0u;
                    continue;
                }

                int blockLevel = blockAccessor.GetLightLevel(sampleX, sampleY, sampleZ, EnumLightLevelType.OnlyBlockLight);
                int sunLevel = blockAccessor.GetLightLevel(sampleX, sampleY, sampleZ, EnumLightLevelType.OnlySunLight);

                int rgb = blockAccessor.GetLightRGBsAsInt(sampleX, sampleY, sampleZ);
                int lightId = GetOrAssignLightId(rgb);

                // v1 material palette: stable placeholder derived from block id (does not encode per-face variation yet).
                int materialPaletteIndex = block.Id & (int)LumonSceneOccupancyPacking.MaterialPaletteIndexMask;

                outData[idx++] = LumonSceneOccupancyPacking.PackClamped(blockLevel, sunLevel, lightId, materialPaletteIndex);
            }
        }
    }

    private int GetOrAssignLightId(int rgb)
    {
        // Quantize RGB to reduce churn. 3 bits/channel -> 512 possible keys.
        int r = (rgb >> 16) & 0xFF;
        int g = (rgb >> 8) & 0xFF;
        int b = rgb & 0xFF;

        int rq = r >> 5;
        int gq = g >> 5;
        int bq = b >> 5;

        int key = (rq << 6) | (gq << 3) | bq;

        if (lightKeyToId.TryGetValue(key, out byte existing))
        {
            return existing;
        }

        // Reserve 0 as a fallback. Allocate new ids from 1..63.
        int nextId = lightKeyToId.Count + 1;
        if (nextId >= LumonSceneOccupancyClipmapGpuResources.MaxLightColors)
        {
            return 0;
        }

        byte id = (byte)nextId;
        lightKeyToId[key] = id;

        float rf = rq / 7.0f;
        float gf = gq / 7.0f;
        float bf = bq / 7.0f;

        int o = id * 4;
        lightLutData[o + 0] = rf;
        lightLutData[o + 1] = gf;
        lightLutData[o + 2] = bf;
        lightLutData[o + 3] = 1.0f;

        lightLutDirty = true;
        return id;
    }

    private static int Wrap(int v, int mod)
    {
        if (mod <= 0) return 0;
        int m = v % mod;
        return m < 0 ? m + mod : m;
    }
}
