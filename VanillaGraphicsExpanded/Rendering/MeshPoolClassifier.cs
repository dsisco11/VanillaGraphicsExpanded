using System.Collections.Generic;
using System.Runtime.CompilerServices;
using Vintagestory.API.Client;

namespace VanillaGraphicsExpanded.Rendering;

/// <summary>Shares lifecycle-built, reference-identity mesh-pool classification between rendering features.</summary>
internal sealed class MeshPoolClassifier
{
    private static readonly ConditionalWeakTable<MeshDataPoolManager[][], MeshPoolClassifier> Tables = new();
    private readonly HashSet<MeshDataPoolManager> twoSided = new(ReferenceEqualityComparer.Instance);
    private readonly HashSet<MeshDataPoolManager> displacement = new(ReferenceEqualityComparer.Instance);
    internal static MeshPoolClassifier? Current { get; set; }

    #region Pool lifecycle
    /// <summary>Copies only relevant pass membership; no array traversal is needed during manager draws.</summary>
    private MeshPoolClassifier(MeshDataPoolManager[][] pools)
    {
        Add(pools, EnumChunkRenderPass.OpaqueNoCull, twoSided);
        Add(pools, EnumChunkRenderPass.Opaque, displacement);
        Add(pools, EnumChunkRenderPass.TopSoil, displacement);
    }

    /// <summary>Resolves a table once, also supporting installations after an engine renderer already exists.</summary>
    internal static MeshPoolClassifier For(MeshDataPoolManager[][] pools)
        => Tables.GetValue(pools, static table => new MeshPoolClassifier(table));

    /// <summary>Replaces cached membership after engine construction or runtime atlas additions mutate a table.</summary>
    internal static void Rebuild(MeshDataPoolManager[][] pools)
    {
        Tables.Remove(pools);
        Tables.Add(pools, new MeshPoolClassifier(pools));
    }

    /// <summary>Builds a pass set while tolerating empty or partially initialized tables.</summary>
    private static void Add(MeshDataPoolManager[][] pools, EnumChunkRenderPass pass, HashSet<MeshDataPoolManager> target)
    {
        int index = (int)pass;
        if (index >= pools.Length || pools[index] == null) return;
        foreach (var manager in pools[index])
            if (manager != null) target.Add(manager);
    }
    #endregion

    #region Draw classification
    /// <summary>Tests two-sided membership in expected constant time.</summary>
    internal bool IsTwoSided(MeshDataPoolManager manager) => twoSided.Contains(manager);

    /// <summary>Tests opaque/topsoil displacement membership in expected constant time.</summary>
    internal bool IsDisplacementEligible(MeshDataPoolManager manager) => displacement.Contains(manager);
    #endregion
}
