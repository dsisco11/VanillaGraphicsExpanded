using System;
using VanillaGraphicsExpanded.Numerics;
using Vintagestory.API.Client;
using Vintagestory.Client.NoObf;

namespace VanillaGraphicsExpanded.Rendering.Integration.Shadows;

/// <summary>One batch-local selection from the native pass/atlas table, retaining its original storage owner.</summary>
internal sealed class NativeShadowTerrainGroup : IDisposable
{
    private readonly MeshDataPool pool;
    private readonly VAO mesh;
    private readonly int vertices, indices;

    #region Public API
    internal int PoolId { get; }
    internal EnumChunkRenderPass Pass { get; }
    internal int Atlas { get; }
    internal int TextureId { get; }
    internal int Dimension { get; }
    internal Vector3d Origin { get; }
    internal bool Casts { get; }
    internal NativeShadowTerrainGeometry? Geometry { get; }
    internal int VertexCapacity => vertices;
    internal int IndexCapacity => indices;

    /// <summary>Freezes one ordinary group without touching culling arrays, statistics or individual pool allocations.</summary>
    internal NativeShadowTerrainGroup(MeshDataPool pool, EnumChunkRenderPass pass, int atlas, int textureId)
    {
        this.pool = pool;
        mesh = NativeShadowTerrainFields.Mesh(pool) as VAO
            ?? throw new NotSupportedException("Native pool has no supported realized terrain storage.");
        PoolId = NativeShadowTerrainFields.Id(pool); Pass = pass; Atlas = atlas; TextureId = textureId;
        Casts = NativeShadowTerrainAdapter.Casts(pass);
        Dimension = pool.dimensionId;
        Origin = new(pool.poolOrigin.X, pool.poolOrigin.Y, pool.poolOrigin.Z);
        vertices = pool.VerticesPoolSize; indices = pool.IndicesPoolSize;
        if (Casts) Geometry = new(mesh, vertices, indices);
    }

    /// <summary>Rejects changed native ownership and structural state before a borrowed submission.</summary>
    internal bool IsCurrent => (!Casts || Geometry is { IsPrepared: true }) && ReferenceEquals(NativeShadowTerrainFields.Mesh(pool), mesh)
        && NativeShadowTerrainFields.Id(pool) == PoolId && pool.dimensionId == Dimension
        && pool.VerticesPoolSize == vertices && pool.IndicesPoolSize == indices
        && pool.poolOrigin.X == Origin.X && pool.poolOrigin.Y == Origin.Y && pool.poolOrigin.Z == Origin.Z;

    /// <summary>Ends this batch's borrow without retiring any engine allocation.</summary>
    public void Dispose() { Geometry?.Dispose(); }
    #endregion
}
