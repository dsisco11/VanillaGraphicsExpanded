using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using HarmonyLib;
using Vintagestory.API.Client;

namespace VanillaGraphicsExpanded.Rendering.Diagnostics;

/// <summary>Associates engine pool managers with render-pass names without retaining their lifetime.</summary>
internal static class EngineMeshPoolDebugLabels
{
    private static readonly ConditionalWeakTable<MeshDataPoolManager, PoolName> Names = new();
    private static readonly AccessTools.FieldRef<MeshDataPoolManager, List<MeshDataPool>> Pools = AccessTools.FieldRefAccess<MeshDataPoolManager, List<MeshDataPool>>("pools");
    private static readonly AccessTools.FieldRef<MeshDataPool, MeshRef> Mesh = AccessTools.FieldRefAccess<MeshDataPool, MeshRef>("modelRef");

    /// <summary>Stores only descriptive context; no GPU objects or owning references are retained.</summary>
    private sealed record PoolName(string Value);

    #region Public API
    /// <summary>Refreshes semantic names when the engine creates or extends manager tables.</summary>
    [Conditional("DEBUG")]
    internal static void Register(MeshDataPoolManager[][] tables)
    {
        for (int pass = 0; pass < tables.Length; pass++)
        {
            var managers = tables[pass];
            if (managers is null) continue;
            for (int atlas = 0; atlas < managers.Length; atlas++)
            {
                var manager = managers[atlas];
                if (manager is null) continue;
                Names.Remove(manager);
                Names.Add(manager, new($"VS.MeshPool.{(EnumChunkRenderPass)pass}.Atlas{atlas}"));
                // Normally empty during construction; existing allocations may survive atlas-table growth.
                LabelAdded(manager, Pools(manager), 0);
            }
        }
    }

    /// <summary>Labels only pools appended by an upload; ordinary uploads into existing pools do no GL work.</summary>
    [Conditional("DEBUG")]
    internal static void LabelAdded(MeshDataPoolManager manager, List<MeshDataPool> pools, int previousCount)
    {
        if (pools is null || !Names.TryGetValue(manager, out var name)) return;
        for (int i = previousCount; i < pools.Count; i++)
            EngineMeshDebugLabels.Apply(Mesh(pools[i]), $"{name.Value}.Pool{i}");
    }
    #endregion
}
