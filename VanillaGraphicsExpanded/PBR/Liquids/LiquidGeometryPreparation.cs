using System;
using System.Collections.Generic;
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

    /// <summary>Checks retained geometry receipts before admission; enumerates engine collections only when replacement is required.</summary>
    internal void Prepare(ReadOnlySpan<MeshDataPoolManager> managers)
    {
        // Configuration epochs are independent of canonical metadata. Preserve receipt validation
        // here so a changed VAO is rebuilt before boundary admission, never during a draw.
        // This visits only adapters, not engine membership lists or manager selection snapshots.
        if (!dirty)
            foreach (var value in geometry.Values)
                if (!value.IsCurrent) { dirty = true; break; }
        if (dirty) Rebuild(managers);
    }

    /// <summary>Schedules replacement before the next admitted submission.</summary>
    internal void Invalidate() => dirty = true;

    /// <summary>Clears borrowed preparation while retaining lifecycle subscriptions for world reentry.</summary>
    internal void Clear()
    {
        foreach (var value in geometry.Values) value.Dispose();
        geometry.Clear();
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
        dirty = true;
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
        dirty = false;
    }
    #endregion
}
