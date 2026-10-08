using System;
using System.Collections.Generic;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Rendering.Pipeline;
using VanillaGraphicsExpanded.Rendering.Pipeline.Descriptions;
using VanillaGraphicsExpanded.Rendering.Pipeline.Passes;
using VanillaGraphicsExpanded.Rendering.Shaders;
namespace VanillaGraphicsExpanded.PBR.Postprocessing;
/// <summary>Owns procedural geometry and cached PSOs shared by the postprocess algorithms.</summary>
internal sealed class PostprocessDraw : IDisposable
{
    private static readonly VertexLayoutDesc Layout = new([]);
    private readonly GraphicsPipelineLifetime lifetime = new();
    private readonly Dictionary<GpuProgram,GraphicsPipeline> pipelines = new();
    private readonly ArrayGraphicsGeometry geometry = new(Layout,PrimitiveType.Triangles,new Dictionary<int,GpuVbo>(),proceduralVertices:3);
    #region Public API
    /// <summary>Prepares each executable/target contract before entering the shared graphics boundary.</summary>
    internal GraphicsPipeline Prepare(GpuProgram shader,GpuFramebuffer target)
    {
        using var metadata=new RenderPassTargets(new RenderPassDesc(target,[new(0)]));
        var description=new GraphicsPipelineDesc(shader.GraphicsIdentity!,Layout,metadata.Signature,DynamicPipelineState.Viewport);
        if(pipelines.TryGetValue(shader,out var old)&&old.Description==description&&old.ExecutableRevision==shader.ExecutableRevision) return old;
        var replacement=new GraphicsPipeline(lifetime,description,shader);
        old?.Dispose(); pipelines[shader]=replacement; return replacement;
    }
    /// <summary>Draws a complete target using only declared PSO and pass state.</summary>
    internal void Submit(GraphicsCommandContext commands,GraphicsPipeline pipeline,GpuFramebuffer target)
    {
        commands.BeginPass(new RenderPassDesc(target,[new(0)]));
        commands.SetPipeline(pipeline);
        commands.SetDynamicState(new(){Viewport=commands.PassViewport});
        commands.Draw(geometry,new(0,3)); commands.EndPass();
    }
    /// <summary>Retires executable references before geometry and pipeline lifetime.</summary>
    public void Dispose() { foreach(var pipeline in pipelines.Values) pipeline.Dispose(); pipelines.Clear(); geometry.Dispose(); lifetime.Dispose(); }
    #endregion
}
