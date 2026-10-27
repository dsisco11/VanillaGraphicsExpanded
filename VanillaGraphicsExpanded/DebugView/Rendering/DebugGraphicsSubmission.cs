using VanillaGraphicsExpanded.Rendering.ShaderCompilation;
using System;
using System.Collections.Generic;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Rendering.RHI;
using VanillaGraphicsExpanded.Rendering.Pipeline.Descriptions;
using VanillaGraphicsExpanded.Rendering.Shaders;
using Vintagestory.API.Client;

namespace VanillaGraphicsExpanded.DebugView;

/// <summary>Owns debug-mode pipelines and an explicitly published external destination, leaving native execution to the RHI.</summary>
internal sealed class DebugGraphicsSubmission : IDisposable
{
    private readonly DebugRenderTarget destination;
    private readonly Dictionary<string, GraphicsPipelineAsset> pipelines = new();

    #region Internal API
    /// <summary>Subscribes external destination facts to engine framebuffer publication boundaries.</summary>
    internal DebugGraphicsSubmission(ICoreClientAPI api) => destination = new(api);

    /// <summary>Submits an explicit nonindexed vertex interval with debug raster policy and captured shader inputs.</summary>
    internal bool Draw(string mode, GpuProgram shader, RHIGeometryInputs geometry, RHIDraw vertices,
        PrimitiveTopology topology, bool depthTest = false, float lineWidth = 1, float pointSize = 1,
        bool programPointSize = false, RenderArea? area = null, bool depthWrite = false, IReadOnlyList<ColorBlendDesc>? blending = null)
        => Submit(mode, shader, geometry, vertices, null, topology, depthTest, lineWidth, pointSize, programPointSize, area, depthWrite, blending);

    /// <summary>Submits an explicit indexed interval without translating its producer-owned index contents.</summary>
    internal bool DrawIndexed(string mode, GpuProgram shader, RHIGeometryInputs geometry, RHIDrawIndexed indices,
        PrimitiveTopology topology, bool depthTest = false, float lineWidth = 1, float pointSize = 1,
        bool programPointSize = false, RenderArea? area = null, bool depthWrite = false, IReadOnlyList<ColorBlendDesc>? blending = null)
        => Submit(mode, shader, geometry, null, indices, topology, depthTest, lineWidth, pointSize, programPointSize, area, depthWrite, blending);
    #endregion

    #region Public API
    /// <summary>Retires mode realizations and borrowed integration metadata without disposing shader owners or engine images.</summary>
    public void Dispose()
    {
        foreach (var pipeline in pipelines.Values) pipeline.Dispose();
        pipelines.Clear(); destination.Dispose();
    }
    #endregion

    #region Private
    /// <summary>Prepares the fixed mode contract, admits its exact footprint, then delegates the selected draw family to the RHI.</summary>
    private bool Submit(string mode, GpuProgram shader, RHIGeometryInputs geometry, RHIDraw? vertices, RHIDrawIndexed? indices,
        PrimitiveTopology topology, bool depthTest, float lineWidth, float pointSize, bool programPointSize,
        RenderArea? area, bool depthWrite, IReadOnlyList<ColorBlendDesc>? blending)
    {
        if (!shader.EnsureReady()) return false;
        var target = destination.GetPass(blending?.Count ?? 1);
        var pass = area is null ? target : new RHIRenderPassDescription(target.Surface, target.Colors,
            actions: target.Actions, area: area);
        var commands = RHICommandContext.Current;
        var signature = commands.DescribeRenderTargets(pass);
        var description = new RHIGraphicsPipelineDescription(shader.Shader, geometry.Layout, signature,
            DynamicPipelineState.Viewport, depthStencil: new() { DepthTest = depthTest, DepthWrite = depthWrite, DepthComparison = ComparisonFunction.LessOrEqual },
            rasterizer: new() { LineWidth = lineWidth, PointSize = pointSize, ProgramPointSize = programPointSize },
            assembly: new() { Topology = topology }, blending: blending);
        pipelines.TryGetValue(mode, out var pipeline);
        if (pipeline is null || pipeline.Description != description)
        {
            var replacement = new GraphicsPipelineAsset(shader,description);
            pipeline?.Dispose(); pipelines[mode] = pipeline = replacement;
        }
        if (!commands.TryEnterBoundary(new("Debug." + mode, true, graphics: [pipeline.Pipeline], operations: BoundaryOperations.RenderPasses), out var boundary)) return false;
        using (boundary) boundary!.Run(() =>
        {
            commands.BeginRenderPass(pass); commands.SetPipeline(pipeline.Pipeline);
            commands.SetDynamicState(new() { Viewport = commands.PassViewport });
            commands.BindResources(shader.CaptureBindings());
            if (indices is { } indexed) commands.DrawIndexed(geometry, indexed);
            else commands.Draw(geometry, vertices!.Value);
            commands.EndRenderPass();
        });
        return true;
    }
    #endregion
}
