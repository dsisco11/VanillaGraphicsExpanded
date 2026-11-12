using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using VanillaGraphicsExpanded.Rendering.Shadows.Candidates;
using VanillaGraphicsExpanded.Rendering.Shadows.Configuration;
using VanillaGraphicsExpanded.Rendering.Shadows.Geometry;
using VanillaGraphicsExpanded.Rendering.Shadows.Pages;
using VanillaGraphicsExpanded.Rendering.Shadows.World;
using VanillaGraphicsExpanded.Rendering.Shadows.Scheduling;
using Vintagestory.API.Client;
using Vintagestory.API.Common;

namespace VanillaGraphicsExpanded.Rendering.Integration.Shadows;

/// <summary>Joins native terrain and entity owners into one complete bounded shadow submission without a geometry registry.</summary>
internal sealed class NativeShadowScene : IDisposable
{
    private readonly NativeShadowTerrainScene terrain;
    private readonly NativeShadowEntityScene entities;
    private bool disposed;
    private ShadowFrameBudget? frameBudget;
    private long estimatedTriangles,estimatedInstances;
    private bool estimatesReady;

    #region Public API
    #region Preparation and estimation
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
    internal bool IsCurrent => !disposed && (frameBudget is null || frameBudget.TryCharge(
        new(Inputs:PreparationCost.Inputs,Groups:terrain.Drawing.Intervals.Length))) && terrain.IsCurrent && entities.IsCurrent;
    /// <summary>Preserves the preparation owner's actual change stream instead of accepting unrelated scene revisions.</summary>
    internal ShadowSceneChanges Changes=>entities.Changes;

    /// <summary>Attaches the single frame ledger before repeated native lifetime and range checks begin.</summary>
    internal void UseBudget(ShadowFrameBudget budget)
    {
        if(frameBudget is not null && !ReferenceEquals(frameBudget,budget))throw new InvalidOperationException("A native scene belongs to one frame budget.");
        frameBudget=budget;
        if(!estimatesReady)
        {
            if(!budget.TryCharge(new(Inputs:terrain.Drawing.Ranges.Length+entities.Ranges.Length)))
                throw new InvalidOperationException("Native estimate production exceeds the shared input allowance.");
            PrepareEstimates();
        }
    }

    /// <summary>Charges complete native preparation once, separately from per-view repeated selection and raster work.</summary>
    internal ShadowWork PreparationCost => new(Inputs: terrain.Publication.Table.Length + terrain.Drawing.Ranges.Length + entities.Ranges.Length,
        Groups: terrain.Drawing.Intervals.Length, Bytes: checked(terrain.MetadataBytes + entities.MetadataBytes));

    /// <summary>Conservatively bounds all native geometry per virtual view, including inactive commands and unknown object bounds.</summary>
    internal ShadowWork EstimateWork(IReadOnlyList<ShadowDrawView> views, int requests, int associations, int candidateEntries)
    {
        if(!estimatesReady)PrepareEstimates();
        if(frameBudget is { } budget && !budget.TryCharge(new(Inputs:views.Count+1)))
            throw new InvalidOperationException("Alternative estimate production exceeds the shared input allowance.");
        long triangles=estimatedTriangles,pixels=0,samples=0;
        foreach(var view in views)
        {
            long area=checked(view.Region.Width*(long)view.Region.Height);
            pixels=checked(pixels+area);samples=checked(samples+triangles*area);
        }
        long commands=checked(terrain.Drawing.CommandCount+entities.Ranges.Length);
        long slots=checked(commands*views.Count);
        long draws=checked((terrain.Drawing.Intervals.Length+entities.Ranges.Length)*(long)views.Count);
        // Includes the selector/input copies, coverage checks, private draw captures,
        // one maximum-area raster attachment and immutable projection records.
        long bytes=checked(4096+views.Count*1024L+associations*512L+slots*128L+
            terrain.Drawing.Ranges.Length*128L+entities.Ranges.Length*128L+draws*8192L+pixels);
        // Association coordinates are deduplicated fixed chunk partitions: their
        // spans collectively visit each native terrain range at most once.
        long expansion=checked(associations*(2L+candidateEntries*(1L+terrain.Publication.ProbeLimit)+terrain.Drawing.Ranges.Length)+
            views.Count*(2L*terrain.Drawing.Ranges.Length+entities.Ranges.Length));
        return new(Inputs:checked(draws*64L),Expansion:expansion,
            Groups:draws,Slots:slots,Instances:checked(estimatedInstances*views.Count),Triangles:checked(triangles*views.Count),
            RasterPixels:pixels,RasterSamples:samples,Draws:draws,Clears:requests,Bytes:bytes,Views:views.Count,PageOperations:checked(5L*requests+2));
    }

    #endregion
    #region Selection and submission
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
    #endregion

    #region Private
    /// <summary>Caches a charged scan once; every alternative reuses the same complete native geometry totals.</summary>
    private void PrepareEstimates()
    {
        long triangles=0,instances=0;
        foreach(var range in terrain.Drawing.Ranges)if(range.Eligible!=0)
        {triangles=checked(triangles+range.TriangleCost);instances++;}
        foreach(var range in entities.Ranges)
        {triangles=checked(triangles+range.TriangleCost*(long)range.Instances);instances=checked(instances+range.Instances);}
        estimatedTriangles=triangles;estimatedInstances=instances;estimatesReady=true;
    }
    /// <summary>Prevents a partial or unrelated command array from borrowing a complete native-scene certificate.</summary>
    private void RequireSelection(ShadowGeometrySelection selection)
    {
        if (!IsCurrent || !selection.MatchesInputs(terrain.Drawing.Ranges, entities.Ranges, terrain.Publication))
            throw new InvalidOperationException("GPU selection does not belong to this complete native scene.");
    }
    #endregion
}
