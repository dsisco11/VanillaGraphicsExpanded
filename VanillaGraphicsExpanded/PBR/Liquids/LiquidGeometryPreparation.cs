using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using VanillaGraphicsExpanded.Rendering.Pipeline;
using Vintagestory.API.Client;

namespace VanillaGraphicsExpanded.PBR.Liquids;

/// <summary>Retains only borrowed geometry; engine collections remain the membership authority.</summary>
internal sealed class LiquidGeometryPreparation : IDisposable
{
    private static readonly AccessTools.FieldRef<MeshDataPoolManager, List<MeshDataPool>> ReadPools =
        AccessTools.FieldRefAccess<MeshDataPoolManager, List<MeshDataPool>>("pools");
    private bool dirty = true;
    private Dictionary<MeshDataPool, EngineLiquidPoolGeometry> geometry = new();
    private readonly HashSet<MeshDataPool> withdrawn = new();

    #region Public API
    /// <summary>Gets prepared geometry for a declared engine draw without taking native ownership.</summary>
    internal EngineLiquidPoolGeometry this[MeshDataPool pool] => geometry.TryGetValue(pool, out var value)
        ? value : throw new InvalidOperationException("Engine submitted an unprepared liquid pool.");

    /// <summary>Listens to engine topology changes without retaining a separate membership description.</summary>
    internal LiquidGeometryPreparation()
    {
        LiquidGeometryLifecycle.Changed += Invalidate;
        LiquidGeometryLifecycle.Retiring += RetirePool;
    }

    /// <summary>Rebuilds withdrawn selections before admission; unchanged preparation visits no adapters or engine collections.</summary>
    internal void Prepare(ReadOnlySpan<MeshDataPoolManager> managers)
    {
        if (dirty)
        {
            // Topology changes use the engine's membership authority. Source changes below visit only
            // affected retained adapters; a notification during rebuilding remains queued for next use.
            dirty = false;
            try { Rebuild(managers); }
            catch { dirty = true; throw; }
        }
        else if (withdrawn.Count != 0) RefreshWithdrawn();
    }

    /// <summary>Schedules replacement before the next admitted submission.</summary>
    internal void Invalidate() => dirty = true;

    /// <summary>Clears borrowed preparation while retaining lifecycle subscriptions for world reentry.</summary>
    internal void Clear()
    {
        foreach (var value in geometry.Values) value.Dispose();
        geometry.Clear();
        withdrawn.Clear();
        dirty = true;
    }
    /// <summary>Releases adapters at world/context retirement without disposing engine storage.</summary>
    public void Dispose()
    {
        LiquidGeometryLifecycle.Changed -= Invalidate;
        LiquidGeometryLifecycle.Retiring -= RetirePool;
        Clear();
    }
    #endregion

    #region Private
    /// <summary>Retires one original engine allocation immediately without deleting its storage.</summary>
    private void RetirePool(MeshDataPool pool)
    {
        if (geometry.Remove(pool, out var value)) value.Dispose();
        withdrawn.Remove(pool);
        dirty = true;
    }

    /// <summary>Queues only the adapter whose original resource or configuration was withdrawn.</summary>
    private void WithdrawPool(MeshDataPool pool) => withdrawn.Add(pool);

    /// <summary>Builds affected candidates transactionally without enumerating unrelated engine pools.</summary>
    private void RefreshWithdrawn()
    {
        var pending = withdrawn.ToArray();
        var replacements = new Dictionary<MeshDataPool, EngineLiquidPoolGeometry>();
        try
        {
            foreach (var pool in pending)
            {
                withdrawn.Remove(pool);
                if (!geometry.ContainsKey(pool)) continue;
                var candidate = new EngineLiquidPoolGeometry(pool, () => WithdrawPool(pool));
                replacements.Add(pool, candidate);
                if (!candidate.IsPrepared) throw new InvalidOperationException("Liquid pool changed during preparation.");
            }
        }
        catch
        {
            foreach (var candidate in replacements.Values) candidate.Dispose();
            foreach (var pool in pending) if (geometry.ContainsKey(pool)) withdrawn.Add(pool);
            throw;
        }
        foreach (var replacement in replacements)
        {
            var previous = geometry[replacement.Key];
            geometry[replacement.Key] = replacement.Value;
            previous.Dispose();
        }
    }

    /// <summary>Builds a complete replacement before retiring removed pools, preserving installed entries on failure.</summary>
    private void Rebuild(ReadOnlySpan<MeshDataPoolManager> managers)
    {
        var next = new Dictionary<MeshDataPool, EngineLiquidPoolGeometry>();
        var created = new List<EngineLiquidPoolGeometry>();

        try
        {
            for (int i = 0; i < managers.Length; i++)
            {
                var pools = ReadPools(managers[i]);

                foreach (var pool in pools)
                {
                    if (next.ContainsKey(pool)) continue;
                    withdrawn.Remove(pool);
                    if (!geometry.TryGetValue(pool, out var value) || !value.IsPrepared)
                    {
                        value = new EngineLiquidPoolGeometry(pool, () => WithdrawPool(pool));
                        created.Add(value);
                        if (!value.IsPrepared) throw new InvalidOperationException("Liquid pool changed during preparation.");
                    }
                    next.Add(pool, value);
                }
            }
        }
        catch
        {
            foreach (var value in created) value.Dispose();
            throw;
        }
        foreach (var entry in geometry)
            if (!next.TryGetValue(entry.Key, out var retained) || !ReferenceEquals(entry.Value, retained)) entry.Value.Dispose();
        geometry = next;
    }
    #endregion
}
