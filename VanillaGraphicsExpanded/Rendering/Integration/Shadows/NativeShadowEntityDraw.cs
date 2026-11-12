using System;
using VanillaGraphicsExpanded.Rendering.Pipeline.Descriptions;
using VanillaGraphicsExpanded.Rendering.RHI;
using VanillaGraphicsExpanded.Rendering.Shadows.Geometry;
using VanillaGraphicsExpanded.Rendering.Shadows.Pages;
using Vintagestory.Client.NoObf;

namespace VanillaGraphicsExpanded.Rendering.Integration.Shadows;

/// <summary>Prepares one native texture submesh against a virtual view after native animation preparation.</summary>
internal sealed class NativeShadowEntityDraw : ShadowPreparedDraw
{
    private readonly NativeShadowShapeSnapshot shape;
    private readonly NativeShadowEntityGeometry native;
    private readonly ShadowGeometrySelection selection;
    private readonly ShadowViewBuffer views;
    private readonly ShadowDrawView view;
    private readonly ShadowPagePool pages;
    private readonly int part, texture;
    private readonly VAO mesh;
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
    internal override bool IsCurrent => !disposed && !withdrawn && shape.IsCurrent && native.IsCurrent && atlas.IsValid &&
        views.Matches(ViewIndex, view, pages) && selection.MatchesView(ViewIndex, view, pages) &&
        part < shape.Mesh.meshrefs.Length && part < shape.Mesh.textureids.Length &&
        ReferenceEquals(shape.Mesh.meshrefs[part], mesh) && shape.Mesh.textureids[part] == texture;

    /// <summary>Captures native geometry and material before replacement storage is cleared.</summary>
    internal NativeShadowEntityDraw(NativeShadowShapeSnapshot shape, int part, ShadowEntityShader shader,
        ShadowDrawView view, ShadowViewBuffer views, int viewIndex, ShadowGeometrySelection selection,
        int objectRangeIndex, ShadowPagePool pages)
    {
        if (!shape.IsCurrent || !views.Matches(viewIndex, view, pages) || !selection.MatchesView(viewIndex, view, pages) ||
            (uint)part >= (uint)shape.Mesh.meshrefs.Length ||
            (uint)part >= (uint)shape.Mesh.textureids.Length || shape.Mesh.meshrefs[part] is not VAO realized)
            throw new InvalidOperationException("Native entity submesh is unavailable.");
        this.shape = shape; this.part = part; mesh = realized; texture = shape.Mesh.textureids[part];
        this.selection = selection; this.views = views; this.view = view; this.pages = pages;
        var range = selection.ObjectRange(objectRangeIndex);
        if (range.IndexStart != 0 || range.IndexCount != mesh.IndicesCount || range.BaseVertex != 0 || range.Instances != 1)
            throw new InvalidOperationException("Native entity submesh does not match its complete input command.");
        int command = checked((int)range.Command);
        ViewIndex = viewIndex; Interval = new(0, command, 1);
        native = new(mesh);
        try
        {
            atlas = new(texture);
            geometry = native.WithView(selection.ObjectSelector(viewIndex, command));
            commands = selection.Commands(viewIndex, Interval);
            pass = views.DescribePass(viewIndex);
            shape.Apply(shader, view);
            var material = (IShadowEntityBindings)shader;
            material.Views = views.Texture; material.Atlas = atlas;
            material.Entries = pages.Entries; material.Writable = pages.WritableAssignments;
            material.Depth = new(pages.DepthArray, ImageAccess.ReadWrite, Layered: true, Format: TextureFormat.R32ui);
            bindings = shader.CaptureBindings();
            var rhi = RHICommandContext.Current;
            Pipeline = rhi.CreateGraphicsPipeline(new(shader.Shader, geometry.Layout,
                rhi.DescribeRenderTargets(pass), DynamicPipelineState.Viewport,
                rasterizer: new() { Cull = false }, label: "Shadows.NativeEntity")).RequireResource();
            atlas.ObservePreparation(this, Withdraw);
        }
        catch { Pipeline?.Dispose(); atlas?.Dispose(); native.Dispose(); throw; }
    }

    /// <inheritdoc />
    internal override void Submit()
    {
        if (!IsCurrent) throw new InvalidOperationException("Native entity changed after shadow preparation.");
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
        disposed = true; Pipeline.Dispose(); atlas.Dispose(); native.Dispose();
    }
    #endregion

    #region Private
    /// <summary>Withdraws this captured material when its native texture allocation changes.</summary>
    private void Withdraw() { withdrawn = true; }
    #endregion
}
