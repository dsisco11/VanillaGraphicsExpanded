using System;
using System.Collections.Immutable;
using VanillaGraphicsExpanded.Rendering.Shadows.Configuration;
using Vintagestory.API.Client;
using Vintagestory.Client.NoObf;

namespace VanillaGraphicsExpanded.Rendering.Integration.Shadows;

/// <summary>Derives compatible groups directly from the native pass/atlas table once per bounded batch.</summary>
internal static class NativeShadowTerrainAdapter
{
    #region Public API
    /// <summary>Captures table-owned groups without scanning pool allocations or retaining category mappings.</summary>
    internal static NativeShadowTerrainBatch Capture(ChunkRenderer renderer, ShadowDrawSettings settings)
    {
        ArgumentNullException.ThrowIfNull(renderer);
        var policy = settings.Freeze();
        var groups = ImmutableArray.CreateBuilder<NativeShadowTerrainGroup>();
        var table = NativeShadowTerrainFields.Table(renderer);
        var textures = NativeShadowTerrainFields.Textures(renderer);
        int visits = 0;
        string? failure = null;
        try
        {
            if (table is null || textures is null) throw new InvalidOperationException("Terrain table is unavailable.");
            // Count table cells and pools, including skipped categories. Limits constrain
            // preparation itself, not merely the groups surviving material eligibility.
            for (int pass = 0; pass < table.Length; pass++)
            {
                if (++visits > policy.GroupVisits) throw new InvalidOperationException("Terrain group visit allowance exhausted.");
                var atlases = table[pass];
                if (atlases is null) continue;
                for (int atlas = 0; atlas < atlases.Length; atlas++)
                {
                    if (++visits > policy.GroupVisits) throw new InvalidOperationException("Terrain group visit allowance exhausted.");
                    var manager = atlases[atlas];
                    if (manager is null) continue;
                    foreach (var pool in NativeShadowTerrainFields.Pools(manager))
                    {
                        if (++visits > policy.GroupVisits) throw new InvalidOperationException("Terrain group visit allowance exhausted.");
                        if (atlas >= textures.Length || textures[atlas] <= 0 || pool is null)
                            throw new InvalidOperationException("Terrain material or pool publication is unavailable.");
                        groups.Add(new(pool, (EnumChunkRenderPass)pass, atlas, textures[atlas]));
                    }
                }
            }
        }
        catch (Exception error) when (error is ArgumentException or InvalidOperationException or NotSupportedException)
        {
            failure = error.Message;
            foreach (var group in groups) group.Dispose();
            groups.Clear();
        }
        return new(groups.ToImmutable(), Math.Min(visits, policy.GroupVisits), failure);
    }

    /// <summary>Matches the installed native binary-shadow pass selection; transparent illumination remains separate.</summary>
    internal static bool Casts(EnumChunkRenderPass pass) => pass is EnumChunkRenderPass.Opaque
        or EnumChunkRenderPass.OpaqueNoCull or EnumChunkRenderPass.BlendNoCull or EnumChunkRenderPass.TopSoil;
    #endregion
}
