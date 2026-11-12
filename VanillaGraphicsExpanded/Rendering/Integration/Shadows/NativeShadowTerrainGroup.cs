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
    private readonly ClientPlatformAbstract? platform;
    private readonly ChunkRenderer? renderer;
    private readonly int[]? textureIds;

    #region Public API
    internal int PoolId { get; }
    internal EnumChunkRenderPass Pass { get; }
    internal int Atlas { get; }
    internal int TextureId { get; }
    internal int Dimension { get; }
    internal Vector3d Origin { get; }
    internal bool Casts { get; }
    internal bool Storage { get; }
    internal NativeShadowTerrainGeometry? Geometry { get; }
    internal int VertexCapacity => vertices;
    internal int IndexCapacity => indices;

    /// <summary>Freezes one ordinary group without touching culling arrays, statistics or individual pool allocations.</summary>
    internal NativeShadowTerrainGroup(MeshDataPool pool, EnumChunkRenderPass pass, int atlas, int textureId,
        bool storage = false, ClientPlatformAbstract? platform = null, ChunkRenderer? renderer = null)
    {
        this.pool = pool;
        mesh = NativeShadowTerrainFields.Mesh(pool) as VAO
            ?? throw new NotSupportedException("Native pool has no supported realized terrain storage.");
        PoolId = NativeShadowTerrainFields.Id(pool); Pass = pass; Atlas = atlas; TextureId = textureId;
        Casts = NativeShadowTerrainAdapter.Casts(pass);
        Storage = storage;
        this.platform = platform;
        this.renderer = renderer;
        textureIds = renderer is null ? null : NativeShadowTerrainFields.Textures(renderer);
        Dimension = NativeShadowTerrainFields.Dimension(pool);
        var origin = NativeShadowTerrainFields.Origin(pool)
            ?? throw new NotSupportedException("Native pool has no spatial origin.");
        Origin = new(origin.X, origin.Y, origin.Z);
        vertices = pool.VerticesPoolSize; indices = pool.IndicesPoolSize;
        if (Casts) Geometry = new(mesh, vertices, indices, storage);
    }

    /// <summary>Rejects changed native ownership and structural state before a borrowed submission.</summary>
    internal bool IsCurrent => (platform is null || platform.UseSSBOs == Storage) &&
        (renderer is null || (ReferenceEquals(NativeShadowTerrainFields.Platform(renderer), platform) &&
            ReferenceEquals(NativeShadowTerrainFields.Textures(renderer), textureIds) &&
            textureIds is not null && (uint)Atlas < (uint)textureIds.Length && textureIds[Atlas] == TextureId)) &&
        (!Casts || Geometry is { IsPrepared: true }) && ReferenceEquals(NativeShadowTerrainFields.Mesh(pool), mesh)
        && NativeShadowTerrainFields.Id(pool) == PoolId && NativeShadowTerrainFields.Dimension(pool) == Dimension
        && pool.VerticesPoolSize == vertices && pool.IndicesPoolSize == indices
        && NativeShadowTerrainFields.Origin(pool) is { } origin
        && origin.X == Origin.X && origin.Y == Origin.Y && origin.Z == Origin.Z;

    /// <summary>Ends this batch's borrow without retiring any engine allocation.</summary>
    public void Dispose() { Geometry?.Dispose(); }
    #endregion
}
