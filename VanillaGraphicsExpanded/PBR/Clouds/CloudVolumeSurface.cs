using System;
using System.Collections.Generic;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Rendering.Integration;
using VanillaGraphicsExpanded.Rendering.Pipeline;
using VanillaGraphicsExpanded.Rendering.Pipeline.Descriptions;
using VanillaGraphicsExpanded.Rendering.Pipeline.Passes;
using VanillaGraphicsExpanded.Rendering.Shaders;

namespace VanillaGraphicsExpanded.PBR.Clouds;

/// <summary>Owns one disjoint three-layer view, its framebuffer images and its replacement-write pipeline.</summary>
internal sealed class CloudVolumeSurface : IDisposable
{
    internal readonly GpuTextureView View;
    internal readonly GpuFramebuffer Target;
    private readonly GpuFramebufferAttachment[] images=new GpuFramebufferAttachment[3];
    private readonly Dictionary<GpuProgram,GraphicsPipeline> pipelines=new(ReferenceEqualityComparer.Instance);
    private readonly RenderPassDesc pass;
    internal static readonly RenderPassColor[] Outputs=[new(0),new(1),new(2)];

    #region Public API
    /// <summary>Exposes exactly one payload block and preattaches its three local layers without owning the array.</summary>
    internal CloudVolumeSurface(DynamicTexture3D array,int blockIndex,string name)
    {
        ArgumentNullException.ThrowIfNull(array);
        if(array.TextureTarget!=TextureTarget.Texture2DArray || array.InternalFormat!=PixelInternalFormat.Rgba32f
            || (uint)blockIndex >= (uint)(array.Depth/3)) throw new ArgumentOutOfRangeException(nameof(blockIndex));
        View=GpuTextureView.CreateFromTexture(array,TextureTarget.Texture2DArray,PixelInternalFormat.Rgba32f,0,1,blockIndex*3,3,name);
        try
        {
            // Layer indices are relative to the restricted view; each FBO is configured once.
            for(int i=0;i<images.Length;i++) images[i]=GpuFramebufferAttachment.FromTexture(View,layer:i);
            Target=StaticGpuFramebuffer.Create(images,debugName:$"{name}.Framebuffer");
            pass=new(Target,Outputs);
        }
        catch
        {
            var cleanup=new List<Action>();
            // A failed declaration must release immutable image guards before retiring the images.
            if(Target is not null) cleanup.Add(Target.Dispose);
            foreach(var image in images) if(image is not null) cleanup.Add(image.Dispose);
            cleanup.Add(View.Dispose); RenderingCleanup.Run(cleanup.ToArray()); throw;
        }
    }
    /// <summary>Reuses a complete replacement-write pipeline until its executable or attachment contract changes.</summary>
    internal (RenderPassDesc Pass,GraphicsPipeline Pipeline) Prepare(GraphicsPipelineLifetime lifetime,GpuProgram program)
    {
        using var metadata=new RenderPassTargets(pass);
        var description=new GraphicsPipelineDesc(program.GraphicsIdentity!,CloudVolumeRenderer.Layout,metadata.Signature,DynamicPipelineState.Viewport);
        // Rotation changes a block's role. Retain both executables instead of replacing its PSO every frame.
        if(!pipelines.TryGetValue(program,out var pipeline)||pipeline.Description!=description||pipeline.ExecutableRevision!=program.ExecutableRevision)
        {
            var replacement=new GraphicsPipeline(lifetime,description,program); pipeline?.Dispose();
            pipelines[program]=pipeline=replacement;
        }
        return(pass,pipeline);
    }
    /// <summary>Releases owned pipelines and framebuffer metadata before retiring the view, preserving its source array.</summary>
    public void Dispose()
    {
        var actions=new List<Action>();
        foreach(var pipeline in pipelines.Values) actions.Add(pipeline.Dispose);
        actions.Add(Target.Dispose);
        foreach(var image in images) actions.Add(image.Dispose);
        actions.Add(View.Dispose);
        RenderingCleanup.Run(actions.ToArray());
    }
    #endregion
}
