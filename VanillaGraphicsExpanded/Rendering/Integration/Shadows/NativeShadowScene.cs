using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using VanillaGraphicsExpanded.Rendering.Shadows.Candidates;
using VanillaGraphicsExpanded.Rendering.Shadows.Configuration;
using VanillaGraphicsExpanded.Rendering.Shadows.Geometry;
using VanillaGraphicsExpanded.Rendering.Shadows.Pages;
using VanillaGraphicsExpanded.Rendering.Shadows.World;
using Vintagestory.API.Client;
using Vintagestory.API.Common;

namespace VanillaGraphicsExpanded.Rendering.Integration.Shadows;

/// <summary>Joins native terrain and entity owners into one complete bounded shadow submission without a geometry registry.</summary>
internal sealed class NativeShadowScene : IDisposable
{
    private readonly NativeShadowTerrainScene terrain;
    private readonly NativeShadowEntityScene entities;
    private bool disposed;

    #region Public API
    /// <summary>Takes ownership of already prepared native snapshots whose command intervals form one complete scene.</summary>
    internal NativeShadowScene(NativeShadowTerrainScene terrain, NativeShadowEntityScene entities)
    {
        this.terrain = terrain; this.entities = entities;
        for (int index = 0; index < entities.Ranges.Length; index++)
            if (entities.Ranges[index].Command != checked(terrain.Drawing.CommandCount + index))
                throw new InvalidOperationException("Native entity commands must follow the complete terrain interval.");
        if (!IsCurrent) throw new InvalidOperationException("Native scene preparation is incomplete.");
    }

    /// <summary>Requires both native owners to certify the same still-current captured inputs.</summary>
    internal bool IsCurrent => !disposed && terrain.IsCurrent && entities.IsCurrent;

    /// <summary>Charges aggregate scene and view metadata before constructing GPU-only selection over complete native commands.</summary>
    internal ShadowGeometrySelection Select(ICoreAPI api, IReadOnlyList<ShadowDrawView> views,
        IReadOnlyList<ShadowViewCandidates> associations, ShadowCandidatePool candidates, ShadowWorldGpuBuffers world,
        ShadowViewBuffer viewBuffer, ShadowPagePool pages, ShadowDrawSettings settings)
    {
        if (!IsCurrent) throw new InvalidOperationException("Native scene changed before selection.");
        // Per-draw allowance includes native placement, captured material, private
        // binding/pipeline descriptions and temporary submission references.
        long bytes = checked(terrain.MetadataBytes + entities.MetadataBytes + viewBuffer.AllocatedBytes +
            terrain.Drawing.Intervals.Length * (long)views.Count * 4096 + entities.Ranges.Length * (long)views.Count * 4096);
        return new(api, views, associations, candidates, terrain.Publication, world, terrain.Drawing.Ranges,
            checked(terrain.Drawing.CommandCount + entities.Ranges.Length), pages, settings, bytes, entities.Ranges, terrain.Padding);
    }

    /// <summary>Prepares complete native intervals against frozen virtual views before any replacement is admitted.</summary>
    internal ImmutableArray<ShadowPreparedDraw> Prepare(IClientWorldAccessor world, IReadOnlyList<ShadowDrawView> views,
        ShadowViewBuffer viewBuffer, ShadowGeometrySelection selection, ShadowPagePool pages,
        System.Func<bool, bool, ShadowNativeMaterialShader> terrainShader, ShadowEntityShader bodyShader, ShadowStaticShader itemShader,
        float deltaTime, float paddingX, float paddingY)
    {
        RequireSelection(selection);
        if (bodyShader.Waving != (terrain.Waving ? 1 : 0) || itemShader.Waving != (terrain.Waving ? 1 : 0))
            throw new InvalidOperationException("Native entity materials differ from the captured scene deformation policy.");
        var terrainDraws = terrain.Prepare(world, views, viewBuffer, selection, pages, terrainShader, deltaTime, paddingX, paddingY);
        ImmutableArray<ShadowPreparedDraw> entityDraws = [];
        try
        {
            entityDraws = entities.Prepare(bodyShader, itemShader, views, viewBuffer, selection, pages);
            return terrainDraws.AddRange(entityDraws);
        }
        catch
        {
            foreach (var draw in entityDraws) draw.Dispose();
            foreach (var draw in terrainDraws) draw.Dispose();
            throw;
        }
    }

    /// <summary>Holds native chunk lifetime authority across admission, frozen clears, all draws and whole-page completion.</summary>
    internal void Submit(ShadowPagePool pages, ShadowGeometrySelection selection,
        IReadOnlyList<ShadowPageReplacement> replacements, IReadOnlyList<ShadowPreparedDraw> draws,
        ShadowSceneChanges changes, ShadowChangeBatch scene, double timestamp)
    {
        RequireSelection(selection);
        if (!terrain.TryUse(() => ShadowDepthSubmission.Submit(pages, selection, replacements, draws,
                changes, scene, () => IsCurrent, timestamp)))
            throw new InvalidOperationException("Native scene lifetime was withdrawn before submission.");
    }

    /// <summary>Releases snapshot borrows only after callers retire their draws and selection fences.</summary>
    public void Dispose()
    {
        if (disposed) return;
        disposed = true; entities.Dispose(); terrain.Dispose();
    }
    #endregion

    #region Private
    /// <summary>Prevents a partial or unrelated command array from borrowing a complete native-scene certificate.</summary>
    private void RequireSelection(ShadowGeometrySelection selection)
    {
        if (!IsCurrent || !selection.MatchesInputs(terrain.Drawing.Ranges, entities.Ranges, terrain.Publication))
            throw new InvalidOperationException("GPU selection does not belong to this complete native scene.");
    }
    #endregion
}
