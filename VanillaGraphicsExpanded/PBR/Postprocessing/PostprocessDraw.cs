using VanillaGraphicsExpanded.Rendering.ShaderCompilation;
using VanillaGraphicsExpanded.Rendering.RHI;
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
    private static readonly RHIVertexLayout Layout = new([]);
    private readonly Dictionary<GpuProgram,GraphicsPipelineAsset> pipelines = new();
    private readonly ArrayGraphicsGeometry geometry = new(Layout,PrimitiveTopology.Triangles,new Dictionary<int,GpuVbo>(),proceduralVertices:3);
    #region Public API
    /// <summary>Prepares each executable/target contract before entering the shared graphics boundary.</summary>
    internal RHIGraphicsPipeline Prepare(GpuProgram shader,RHIRenderPassDescription pass)
    {
        var targets=RHICommandContext.Current.DescribeRenderTargets(pass);
        var description=new RHIGraphicsPipelineDescription(shader.Shader,Layout,targets,DynamicPipelineState.Viewport);
        if(pipelines.TryGetValue(shader,out var old)&&old.Description==description&&old.Description.Shader == shader.Shader) return old.Pipeline;
        var replacement=new GraphicsPipelineAsset(shader,description);
        old?.Dispose(); pipelines[shader]=replacement; return replacement.Pipeline;
    }
    /// <summary>Draws a complete target using only declared PSO and pass state.</summary>
    internal void Submit(RHICommandContext commands,RHIGraphicsPipeline pipeline,RHIRenderPassDescription pass,RHIResourceBindings inputs)
    {
        commands.BeginRenderPass(pass);
        commands.SetPipeline(pipeline);
        commands.SetDynamicState(new(){Viewport=commands.PassViewport});
        commands.BindResources(inputs);
        commands.Draw(geometry.Inputs,new(0,3)); commands.EndRenderPass();
    }
    /// <summary>Retires executable references before geometry and pipeline lifetime.</summary>
    public void Dispose() { foreach(var pipeline in pipelines.Values) pipeline.Dispose(); pipelines.Clear(); geometry.Dispose(); }
    #endregion
}
