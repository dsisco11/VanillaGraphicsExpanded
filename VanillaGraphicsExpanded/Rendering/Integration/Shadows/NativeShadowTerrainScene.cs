using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using VanillaGraphicsExpanded.PBR.Materials;
using VanillaGraphicsExpanded.Rendering.Shadows.Configuration;
using VanillaGraphicsExpanded.Rendering.Shadows.Geometry;
using VanillaGraphicsExpanded.Rendering.Shadows.Pages;
using VanillaGraphicsExpanded.Rendering.Shadows.World;
using Vintagestory.API.Client;
using Vintagestory.Client.NoObf;

namespace VanillaGraphicsExpanded.Rendering.Integration.Shadows;

/// <summary>Owns one bounded native terrain capture from category traversal through complete drawing.</summary>
internal sealed class NativeShadowTerrainScene : IDisposable
{
    private readonly NativeShadowWorldAdapter owner;
    private readonly ShadowWorldPublication world;
    private readonly NativeShadowTerrainBatch batch;
    private readonly NativeShadowTerrainDetail detail;
    private readonly NativeShadowTerrainSubdivision?[] subdivisions;
    private readonly uint[] eligibility;
    private readonly NativeShadowWarpSnapshot warp;
    private readonly DefaultShaderUniforms uniforms;
    private readonly int visitAllowance;
    private readonly bool waving;
    private bool disposed;

    #region Public API
    internal NativeShadowDrawRanges Drawing { get; }
    internal ShadowWorldPublication Publication => world;
    internal double Padding { get; }
    internal bool Waving => waving;
    /// <summary>Charges captured range, sorting, eligibility, group and deformation metadata conservatively.</summary>
    internal long MetadataBytes { get; }

    /// <summary>Captures existing pool categories, chunk-owned detail alternatives and native materials once per batch.</summary>
    internal NativeShadowTerrainScene(ChunkRenderer renderer, NativeShadowWorldAdapter owner,
        ShadowWorldPublication world, NativeShadowTerrainDetail detail, MaterialAtlasSystem materials,
        DefaultShaderUniforms uniforms, bool waving, ShadowDrawSettings settings)
    {
        this.owner = owner; this.world = world; this.detail = detail; this.uniforms = uniforms; this.waving = waving;
        var policy = settings.Freeze(); visitAllowance = policy.ExpansionVisits;
        warp = new(uniforms);
        if (!owner.TryCaptureDrawEligibility(world, detail, visitAllowance, out eligibility))
            throw new InvalidOperationException("Complete native terrain eligibility is unavailable.");
        batch = NativeShadowTerrainAdapter.Capture(renderer, policy);
        try
        {
            if (!batch.IsCurrent) throw new InvalidOperationException(batch.Failure ?? "Native terrain capture changed.");
            MetadataBytes = checked(world.Ranges.Length * 80L + batch.Groups.Length * 1024L + 256);
            if (MetadataBytes > policy.MemoryMiB * 1048576L)
                throw new InvalidOperationException("Native terrain metadata exceeds the complete drawing allowance.");
            subdivisions = new NativeShadowTerrainSubdivision?[batch.Groups.Length];
            int amplification = 1;
            double displacement = 0;
            for (int index = 0; index < batch.Groups.Length; index++)
            {
                var group = batch.Groups[index];
                if (!group.Casts) continue;
                var subdivision = new NativeShadowTerrainSubdivision(group, materials);
                subdivisions[index] = subdivision;
                amplification = Math.Max(amplification, subdivision.TriangleAmplification);
                displacement = Math.Max(displacement, subdivision.BoundsPadding);
            }
            Padding = NativeShadowTerrainEnvelope.Capture(uniforms, waving, displacement);
            // Applying the largest amplification to every terrain range is conservative
            // and preserves one range record per native allocation for the GPU selector.
            Drawing = NativeShadowDrawRanges.Create(batch, world, eligibility, amplification, policy);
        }
        catch { batch.Dispose(); throw; }
    }

    /// <summary>Checks native ownership, current detail visibility and material state without rebuilding category membership.</summary>
    internal bool IsCurrent
    {
        get
        {
            if (disposed || !warp.IsCurrent || !batch.IsCurrent ||
                !owner.TryCaptureDrawEligibility(world, detail, visitAllowance, out var current) ||
                !current.AsSpan().SequenceEqual(eligibility)) return false;
            foreach (var subdivision in subdivisions)
                if (subdivision is not null && !subdivision.IsCurrent) return false;
            return true;
        }
    }

    /// <summary>Retains the native chunk mutation gate for the complete clear, draw and publication operation.</summary>
    internal bool TryUse(Action submit)
    {
        ArgumentNullException.ThrowIfNull(submit);
        if (!IsCurrent) return false;
        return owner.TryUse(world, () =>
        {
            if (!IsCurrent) throw new InvalidOperationException("Native terrain changed before submission.");
            submit();
            if (!IsCurrent) throw new InvalidOperationException("Native terrain changed during submission.");
        });
    }

    /// <summary>Creates each CPU-known native group/view draw after GPU selection, never one draw per physical page.</summary>
    internal ImmutableArray<ShadowPreparedDraw> Prepare(IClientWorldAccessor clientWorld,
        IReadOnlyList<ShadowDrawView> views, ShadowViewBuffer viewBuffer, ShadowGeometrySelection selection,
        ShadowPagePool pages, Func<bool, bool, ShadowNativeMaterialShader> shader,
        float deltaTime, float paddingX, float paddingY)
    {
        if (!IsCurrent || selection.TerrainPadding < Padding || views.Count != selection.ViewCount)
            throw new InvalidOperationException("Native terrain scene is no longer selectable.");
        var result = ImmutableArray.CreateBuilder<ShadowPreparedDraw>();
        try
        {
            foreach (var interval in Drawing.Intervals)
            {
                if (interval.Count == 0) continue;
                var group = batch.Groups[interval.Group];
                var subdivision = subdivisions[interval.Group];
                var program = shader(group.Storage, subdivision is { Enabled: true });
                int actualWaving = program switch
                {
                    ShadowTerrainShader ordinary => ordinary.Waving,
                    ShadowDisplacedTerrainShader adaptive => adaptive.Waving,
                    _ => throw new InvalidOperationException("Native terrain material family is unavailable.")
                };
                if (actualWaving != (waving ? 1 : 0))
                    throw new InvalidOperationException("Native terrain deformation policy changed after capture.");
                for (int index = 0; index < views.Count; index++)
                {
                    var placement = new NativeShadowTerrainPlacement(group, clientWorld, views[index].Origin, deltaTime);
                    result.Add(new NativeShadowTerrainDraw(group, placement, uniforms, program, views[index],
                        viewBuffer, index, selection, interval, pages, paddingX, paddingY, subdivision));
                }
            }
            return result.ToImmutable();
        }
        catch { foreach (var draw in result) draw.Dispose(); throw; }
    }

    /// <summary>Retires only private group borrows after the caller has released prepared draws and their fence.</summary>
    public void Dispose()
    {
        if (disposed) return;
        disposed = true; batch.Dispose();
    }
    #endregion
}
