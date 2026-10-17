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

/// <summary>Owns FP32 bucket accumulation, attenuation and depth images for one reduced cloud payload.</summary>
internal sealed class CloudVolumeSurface : IDisposable
{
    internal readonly DynamicTexture2D[] Images=new DynamicTexture2D[6];
    internal readonly GpuFramebuffer Target;
    private GraphicsPipeline? pipeline;
    internal static readonly RenderPassColor[] Outputs=[new(0),new(1),new(2),new(3),new(4),new(5)];

    #region Public API
    /// <summary>Validates bounded allocations before touching GPU storage, using ceil-half extents for odd view sizes.</summary>
    internal static (int Width,int Height) Dimensions(int width,int height)
    {
        if(width is <1 or >16384 || height is <1 or >16384) throw new ArgumentOutOfRangeException(nameof(width));
        int w=(width+1)/2,h=(height+1)/2;
        // Three live sets use 80 bytes per reduced pixel each, including all FP32 metadata.
        if((long)w*h*240>1024L*1024*1024) throw new ArgumentOutOfRangeException(nameof(width),"Cloud targets exceed the one-GiB allocation ceiling.");
        return(w,h);
    }
    /// <summary>Allocates one complete attachment set transactionally; native map/depth images are never owned here.</summary>
    internal CloudVolumeSurface(int width,int height,string name)
    {
        PixelInternalFormat[] formats=[PixelInternalFormat.Rgb32f,PixelInternalFormat.R32f,
            PixelInternalFormat.Rgba32f,PixelInternalFormat.Rgba32f,PixelInternalFormat.Rgba32f,PixelInternalFormat.Rgba32f];
        string[] imageNames=["RevealageBins","Transmission","Accumulation0","Accumulation1","Accumulation2","Depth"];
        try
        {
            for(int i=0;i<Images.Length;i++) Images[i]=DynamicTexture2D.Create(width,height,formats[i],debugName:$"{name}.{imageNames[i]}");
            Target=GpuFramebuffer.CreateMRT(Images,debugName:name)!;
        }
        catch { foreach(var image in Images) image?.Dispose(); throw; }
    }
    /// <summary>Reuses a complete replacement-write pipeline until its executable or attachment contract changes.</summary>
    internal (RenderPassDesc Pass,GraphicsPipeline Pipeline) Prepare(GraphicsPipelineLifetime lifetime,GpuProgram program)
    {
        var pass=new RenderPassDesc(Target,Outputs);
        using var metadata=new RenderPassTargets(pass);
        var description=new GraphicsPipelineDesc(program.GraphicsIdentity!,CloudVolumeRenderer.Layout,metadata.Signature,DynamicPipelineState.Viewport);
        if(pipeline is null||pipeline.Description!=description||!ReferenceEquals(pipeline.Shader,program)||pipeline.ExecutableRevision!=program.ExecutableRevision)
        {
            var replacement=new GraphicsPipeline(lifetime,description,program); pipeline?.Dispose(); pipeline=replacement;
        }
        return(pass,pipeline);
    }
    /// <summary>Releases owned pipelines before framebuffer metadata and attachment storage.</summary>
    public void Dispose()
    {
        var actions=new List<Action>{()=>pipeline?.Dispose(),Target.Dispose};
        foreach(var image in Images) actions.Add(image.Dispose);
        RenderingCleanup.Run(actions.ToArray());
    }
    #endregion
}
