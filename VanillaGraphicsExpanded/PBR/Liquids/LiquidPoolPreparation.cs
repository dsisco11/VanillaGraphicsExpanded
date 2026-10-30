using System;
using System.Collections.Generic;
using HarmonyLib;
using VanillaGraphicsExpanded.Rendering.Pipeline;
using Vintagestory.API.Client;

namespace VanillaGraphicsExpanded.PBR.Liquids;

/// <summary>Retains engine pool membership and prepared geometry between manager lifecycle changes.</summary>
internal sealed class LiquidPoolPreparation : IDisposable
{
    private static readonly AccessTools.FieldRef<MeshDataPoolManager, List<MeshDataPool>> ReadPools =
        AccessTools.FieldRefAccess<MeshDataPoolManager, List<MeshDataPool>>("pools");
    private (MeshDataPoolManager Manager, List<MeshDataPool> Pools, int Count)[] membership = [];
    private Dictionary<MeshDataPool, EngineLiquidPoolGeometry> geometry = new();

    #region Public API
    /// <summary>Gets prepared geometry for a declared engine draw without taking native ownership.</summary>
    internal EngineLiquidPoolGeometry this[MeshDataPool pool] => geometry.TryGetValue(pool, out var value)
        ? value : throw new InvalidOperationException("Engine submitted an unprepared liquid pool.");

    /// <summary>Rebuilds membership only on manager/list replacement or engine pool growth and refreshes stale geometry.</summary>
    internal void Prepare(ReadOnlySpan<MeshDataPoolManager> managers)
    {
        // The installed manager only appends in AddModel; retirement replaces managers on world exit.
        // List identity and count also detect wholesale list replacement and shrinking without a pool scan.
        bool changed = membership.Length != managers.Length;
        for (int i = 0; !changed && i < managers.Length; i++)
            changed = !ReferenceEquals(membership[i].Manager, managers[i])
                || !ReferenceEquals(membership[i].Pools, ReadPools(managers[i]))
                || membership[i].Count != membership[i].Pools.Count;
        if (changed) Rebuild(managers);
        else
        {
            // Geometry validity remains authoritative; draw-time bounds and culling are never cached here.
            foreach (var pools in membership)
                foreach (var pool in pools.Pools)
                    if (!geometry[pool].IsCurrent)
                    {
                        Rebuild(managers);
                        return;
                    }
        }
    }

    /// <summary>Releases adapters at world/context retirement without disposing engine storage.</summary>
    public void Dispose()
    {
        foreach (var value in geometry.Values) value.Dispose();
        geometry.Clear();
        membership = [];
    }
    #endregion

    #region Private
    /// <summary>Builds a complete replacement before retiring removed pools, preserving installed entries on failure.</summary>
    private void Rebuild(ReadOnlySpan<MeshDataPoolManager> managers)
    {
        var next = new Dictionary<MeshDataPool, EngineLiquidPoolGeometry>();
        var created = new List<EngineLiquidPoolGeometry>();
        var members = new (MeshDataPoolManager, List<MeshDataPool>, int)[managers.Length];
        try
        {
            for (int i = 0; i < managers.Length; i++)
            {
                var pools = ReadPools(managers[i]);
                members[i] = (managers[i], pools, pools.Count);
                foreach (var pool in pools)
                {
                    if (next.ContainsKey(pool)) continue;
                    if (!geometry.TryGetValue(pool, out var value) || !value.IsCurrent)
                    {
                        value = new EngineLiquidPoolGeometry(pool);
                        created.Add(value);
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
        membership = members;
    }
    #endregion
}
