using System;
using VanillaGraphicsExpanded.Rendering.Pipeline.Descriptions;
using VanillaGraphicsExpanded.Rendering.RHI;
using VanillaGraphicsExpanded.Rendering.Shadows.Geometry;
using VanillaGraphicsExpanded.Rendering.Shadows.Pages;

namespace VanillaGraphicsExpanded.Rendering.Integration.Shadows;

/// <summary>Submits an owner-prepared standard object with its original complete instance stream.</summary>
internal sealed class NativeShadowStaticDraw : ShadowPreparedDraw
{
    private readonly NativeShadowStaticGeometry native;
    private readonly ShadowGeometrySelection selection;
    private readonly ShadowViewBuffer views;
    private readonly ShadowDrawView view;
    private readonly ShadowPagePool pages;
    private readonly Func<bool> ownerCurrent;
    private readonly BorrowedTexture atlas;
    private readonly RHIGeometryInputs geometry;
    private readonly RHIResourceBindings bindings;
    private readonly RHIIndirectDraw commands;
    private readonly RHIRenderPassDescription pass;
    private bool disposed, withdrawn;

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
    internal override bool IsCurrent => !disposed && !withdrawn && native.IsCurrent && ownerCurrent() && atlas.IsValid &&
        views.Matches(ViewIndex, view, pages) && selection.MatchesView(ViewIndex, view, pages);

    /// <summary>Captures a material already prepared by its native owner and validates the complete original instance interval.</summary>
    internal NativeShadowStaticDraw(NativeShadowStaticGeometry native, int texture, Func<bool> ownerCurrent,
        ShadowStaticShader shader, ShadowDrawView view, ShadowViewBuffer views, int viewIndex,
        ShadowGeometrySelection selection, int objectRangeIndex, ShadowPagePool pages)
    {
        ArgumentNullException.ThrowIfNull(ownerCurrent);
        var range = selection.ObjectRange(objectRangeIndex);
        if (!native.IsCurrent || !ownerCurrent() || shader.Instanced != (native.Instanced ? 1 : 0) ||
            !views.Matches(viewIndex, view, pages) || !selection.MatchesView(viewIndex, view, pages) ||
            range.IndexStart != 0 || range.IndexCount != native.IndexCount || range.BaseVertex != 0 || range.Instances != native.Instances)
            throw new InvalidOperationException("Native object storage, material or complete command interval is unavailable.");
        this.native = native; this.ownerCurrent = ownerCurrent; this.selection = selection;
        this.views = views; this.view = view; this.pages = pages;
        ViewIndex = viewIndex; Interval = new(0, checked((int)range.Command), 1);
        atlas = new(texture);
        try
        {
            geometry = native.WithView(selection.ObjectSelector(viewIndex, Interval.FirstCommand));
            commands = selection.Commands(viewIndex, Interval); pass = views.DescribePass(viewIndex);
            var material = (IShadowStaticBindings)shader;
            material.Views = views.Texture; material.Atlas = atlas; material.Entries = pages.Entries;
            material.Writable = pages.WritableAssignments;
            material.Bank0 = Image(pages, 0); material.Bank1 = Image(pages, 1);
            material.Bank2 = Image(pages, 2); material.Bank3 = Image(pages, 3);
            bindings = shader.CaptureBindings();
            var rhi = RHICommandContext.Current;
            Pipeline = rhi.CreateGraphicsPipeline(new(shader.Shader, geometry.Layout,
                rhi.DescribeRenderTargets(pass), DynamicPipelineState.Viewport,
                rasterizer: new() { Cull = false }, label: "Shadows.NativeObject")).RequireResource();
            atlas.ObservePreparation(this, Withdraw);
        }
        catch { Pipeline?.Dispose(); atlas.Dispose(); throw; }
    }

    /// <inheritdoc />
    internal override void Submit()
    {
        if (!IsCurrent) throw new InvalidOperationException("Native object changed after shadow preparation.");
        var rhi = RHICommandContext.Current;
        rhi.BeginRenderPass(pass);
        try
        {
            rhi.SetPipeline(Pipeline); rhi.SetDynamicState(new() { Viewport = rhi.PassViewport });
            rhi.BindResources(bindings); rhi.DrawIndexedIndirect(geometry, commands);
        }
        finally { rhi.EndRenderPass(); }
    }

    /// <inheritdoc />
    public override void Dispose()
    {
        if (disposed) return;
        disposed = true; Pipeline.Dispose(); atlas.Dispose();
    }
    #endregion

    #region Private
    /// <summary>Withdraws the snapshot when borrowed atlas storage changes.</summary>
    private void Withdraw() { withdrawn = true; }
    /// <summary>Supplies valid resident images while fragment authorization rejects nonexistent destination banks.</summary>
    private static RHIImageBinding Image(ShadowPagePool pages, int bank) =>
        new(pages.DepthBank(bank < pages.ResidentBanks ? bank : 0), ImageAccess.ReadWrite, Format: TextureFormat.R32ui);
    #endregion
}
