using System;
using System.Collections.Generic;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.Rendering.Pipeline;
using VanillaGraphicsExpanded.Rendering.Pipeline.Descriptions;
using VanillaGraphicsExpanded.Rendering.Pipeline.Passes;
using VanillaGraphicsExpanded.Rendering.Shaders;
using Vintagestory.API.Client;

namespace VanillaGraphicsExpanded.DebugView;

/// <summary>Owns named debug pipeline realizations and the current borrowed engine destination.</summary>
internal sealed class DebugGraphicsSubmission : IDisposable
{
    private readonly DebugRenderTarget destination;
    private readonly GraphicsPipelineLifetime lifetime = new();
    private readonly Dictionary<string, GraphicsPipeline> pipelines = new();

    #region Public API
    /// <summary>Subscribes destination metadata to engine rebuild and window resize notifications.</summary>
    internal DebugGraphicsSubmission(ICoreClientAPI api) => destination = new(api);

    /// <summary>Submits one declared debug mode with complete static state and target-sized dynamic viewport.</summary>
    internal bool Draw(string mode, GpuProgram shader, GraphicsGeometry geometry, VertexLayoutDesc layout,
        GraphicsDraw draw, PrimitiveType topology, bool depthTest = false, float lineWidth = 1,
        float pointSize = 1, bool programPointSize = false, RenderArea? area = null,
        bool depthWrite = false, IReadOnlyList<ColorBlendDesc>? blending = null)
    {
        if (!shader.EnsureReady()) return false;
        var target = destination.GetPass(blending?.Count ?? 1);
        var pass = area is null ? target : new RenderPassDesc(target.Target, target.Colors, area: area);
        using var metadata = new RenderPassTargets(pass);
        var description = new GraphicsPipelineDesc(shader.GraphicsIdentity!, layout, metadata.Signature,
            DynamicPipelineState.Viewport, depthStencil: new() { DepthTest = depthTest, DepthWrite = depthWrite, DepthComparison = DepthFunction.Lequal },
            rasterizer: new() { LineWidth = lineWidth, PointSize = pointSize, ProgramPointSize = programPointSize },
            assembly: new() { Topology = topology }, blending: blending);
        pipelines.TryGetValue(mode, out var pipeline);
        if (pipeline is null || pipeline.Description != description || !ReferenceEquals(pipeline.Shader, shader)
            || pipeline.ExecutableRevision != shader.ExecutableRevision)
        {
            var replacement = new GraphicsPipeline(lifetime, description, shader);
            pipeline?.Dispose();
            pipelines[mode] = pipeline = replacement;
        }
        return GraphicsCommandContext.TryRun("Debug." + mode, [pipeline], true, commands =>
        {
            commands.BeginPass(pass);
            commands.SetPipeline(pipeline);
            commands.SetDynamicState(new() { Viewport = commands.PassViewport });
            commands.Draw(geometry, draw);
            commands.EndPass();
        });
    }

    /// <summary>Retires debug realizations and borrowed wrappers without disposing engine targets or shader owners.</summary>
    public void Dispose()
    {
        foreach (var pipeline in pipelines.Values) pipeline.Dispose();
        pipelines.Clear();
        lifetime.Dispose();
        destination.Dispose();
    }
    #endregion
}
