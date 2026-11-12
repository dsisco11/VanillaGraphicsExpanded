using System;
using VanillaGraphicsExpanded.Rendering.Pipeline.Descriptions;
using VanillaGraphicsExpanded.Rendering.RHI;
using VanillaGraphicsExpanded.Rendering.Shadows.Geometry;
using VanillaGraphicsExpanded.Rendering.Shadows.Pages;
using Vintagestory.API.Client;

namespace VanillaGraphicsExpanded.Rendering.Integration.Shadows;

/// <summary>Prepares one native-compatible terrain interval against a frozen virtual view without changing native ownership.</summary>
internal sealed class NativeShadowTerrainDraw : ShadowPreparedDraw
{
    private readonly NativeShadowTerrainGroup group;
    private readonly NativeShadowTerrainPlacement placement;
    private readonly NativeShadowTerrainSubdivision? subdivision;
    private readonly ShadowGeometrySelection selection;
    private readonly ShadowViewBuffer views;
    private readonly ShadowDrawView view;
    private readonly ShadowPagePool pages;
    private readonly NativeShadowWarpSnapshot warp;
    private readonly BorrowedTexture atlas;
    private readonly RHIGeometryInputs geometry;
    private readonly RHIResourceBindings bindings;
    private readonly RHIIndirectDraw commands;
    private readonly RHIRenderPassDescription pass;
    private bool disposed;
    private bool withdrawn;

    #region Public API
    /// <inheritdoc />
    internal override RHIGraphicsPipeline Pipeline { get; }
    /// <inheritdoc />
    internal override int ViewIndex { get; }
    /// <inheritdoc />
    internal override ShadowDrawInterval Interval { get; }
    /// <inheritdoc />
    internal override ShadowGeometrySelection Selection => selection;
    /// <inheritdoc />
    internal override bool IsCurrent => !disposed && !withdrawn && warp.IsCurrent && group.IsCurrent && placement.IsCurrent && atlas.IsValid &&
        (subdivision is null || subdivision.IsCurrent) && views.Matches(ViewIndex, view, pages) &&
        selection.MatchesView(ViewIndex, view, pages);

    /// <summary>Captures material, atlas and storage before any page clear or native draw submission.</summary>
    internal NativeShadowTerrainDraw(NativeShadowTerrainGroup group, NativeShadowTerrainPlacement placement,
        DefaultShaderUniforms uniforms, ShadowNativeMaterialShader shader, ShadowDrawView view,
        ShadowViewBuffer views, int viewIndex, ShadowGeometrySelection selection,
        ShadowDrawInterval interval, ShadowPagePool pages, float paddingX, float paddingY,
        NativeShadowTerrainSubdivision? subdivision = null)
    {
        if (shader is not (ShadowTerrainShader or ShadowDisplacedTerrainShader) ||
            !group.IsCurrent || group.Geometry is null || !placement.IsCurrent ||
            shader.PackedLayout != group.Storage || !views.Matches(viewIndex, view, pages) ||
            !selection.MatchesView(viewIndex, view, pages) ||
            (subdivision is not null && !subdivision.Matches(group)) ||
            (shader is ShadowDisplacedTerrainShader) != (subdivision is { Enabled: true }))
            throw new InvalidOperationException("Native terrain drawing requires current compatible prepared inputs.");
        bool waving = shader is ShadowTerrainShader ordinary ? ordinary.Waving != 0 : ((ShadowDisplacedTerrainShader)shader).Waving != 0;
        double padding = group.Dimension == 1 ? 0 : NativeShadowTerrainEnvelope.Capture(uniforms, waving, subdivision?.BoundsPadding ?? 0);
        if (selection.TerrainPadding < padding)
            throw new InvalidOperationException("Terrain selection does not cover the native deformation envelope.");
        this.group = group; this.placement = placement;
        warp = new(uniforms);
        this.selection = selection; this.views = views; this.view = view; this.pages = pages;
        ViewIndex = viewIndex; Interval = interval;
        this.subdivision = subdivision;
        commands = selection.Commands(viewIndex, interval);
        geometry = group.Geometry.WithViews(selection.Selectors);
        pass = views.DescribePass(viewIndex);
        atlas = new(group.TextureId);
        try
        {
            NativeShadowTerrainMaterial.Capture(shader, uniforms, placement, view, paddingX, paddingY);
            if (shader is ShadowDisplacedTerrainShader adaptive) subdivision!.Apply(adaptive);
            var material = (IShadowTerrainBindings)shader;
            material.Views = views.Texture; material.Atlas = atlas;
            material.Faces = group.Geometry.Faces;
            material.Entries = pages.Entries; material.Writable = pages.WritableAssignments;
            material.Bank0 = Image(pages, 0); material.Bank1 = Image(pages, 1);
            material.Bank2 = Image(pages, 2); material.Bank3 = Image(pages, 3);
            bindings = shader.CaptureBindings();
            var rhi = RHICommandContext.Current;
            // Native shadow terrain is two-sided. Integer image atomics own depth;
            // this raster pass must neither test nor write a depth attachment.
            Pipeline = rhi.CreateGraphicsPipeline(new(shader.Shader, geometry.Layout,
                rhi.DescribeRenderTargets(pass), DynamicPipelineState.Viewport,
                rasterizer: new() { Cull = false }, assembly: new() { Topology = shader.Topology, PatchVertices = 3 },
                label: "Shadows.NativeTerrain")).RequireResource();
            atlas.ObservePreparation(this, Withdraw);
        }
        catch { Pipeline?.Dispose(); atlas.Dispose(); throw; }
    }

    /// <summary>Rechecks borrowed storage and placement immediately before submitting the CPU-known indirect interval.</summary>
    internal override void Submit()
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        if (!IsCurrent)
            throw new InvalidOperationException("Native terrain changed after draw preparation.");
        var rhi = RHICommandContext.Current;
        rhi.BeginRenderPass(pass);
        try
        {
            rhi.SetPipeline(Pipeline);
            rhi.SetDynamicState(new() { Viewport = rhi.PassViewport });
            rhi.BindResources(bindings);
            rhi.DrawIndexedIndirect(geometry, commands);
        }
        finally { rhi.EndRenderPass(); }
    }

    /// <summary>Releases only private pipeline and atlas references after their submitting boundary has ended.</summary>
    public override void Dispose()
    {
        if (disposed) return;
        disposed = true; Pipeline.Dispose(); atlas.Dispose();
    }
    #endregion

    #region Private
    /// <summary>Rejects a material snapshot after the borrowed atlas changes its storage configuration.</summary>
    private void Withdraw() { withdrawn = true; }

    /// <summary>Uses a resident image for unused bindings while fragment authorization rejects nonexistent banks.</summary>
    private static RHIImageBinding Image(ShadowPagePool pages, int bank) =>
        new(pages.DepthBank(bank < pages.ResidentBanks ? bank : 0), ImageAccess.ReadWrite, Format: TextureFormat.R32ui);
    #endregion
}
