using System;
using System.Collections.Generic;
using System.Numerics;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Rendering.Pipeline;
using VanillaGraphicsExpanded.Rendering.Shaders;
using Vintagestory.API.Client;
namespace VanillaGraphicsExpanded.PBR.Postprocessing;
/// <summary>Owns a normalized multiresolution HDR bloom pyramid independently of pass scheduling.</summary>
internal sealed class BloomRenderer : IDisposable
{
    private readonly List<PostprocessImage> down=new(), up=new();
    private int width,height,levels;
    private BloomShaderProgram? shader;
    private GraphicsPipeline? pipeline;
    #region Public API
    /// <summary>Returns the fully reconstructed half-resolution image.</summary>
    internal DynamicTexture2D Texture=>up.Count>0?up[0].Texture:down[0].Texture;
    /// <summary>Prepares persistent resources only when the effect's storage contract changes.</summary>
    internal GraphicsPipeline Prepare(ICoreClientAPI api,PostprocessDraw draw,int frameWidth,int frameHeight,int levelCount)
    {
        if(width!=frameWidth||height!=frameHeight||levels!=levelCount||down.Count==0)
        {
            Dispose(); width=frameWidth; height=frameHeight; levels=levelCount;
            try {
                int w=Math.Max(1,(width+1)/2), h=Math.Max(1,(height+1)/2);
                for(int i=0;i<levels;i++) {
                    down.Add(new(w,h,$"Bloom.Down.{i}"));
                    if(w==1&&h==1) break;
                    w=Math.Max(1,(w+1)/2); h=Math.Max(1,(h+1)/2);
                }
                for(int i=0;i<down.Count-1;i++) up.Add(new(down[i].Texture.Width,down[i].Texture.Height,$"Bloom.Up.{i}"));
            } catch { Dispose(); throw; }
        }
        shader=GpuShaderPrograms.Get<BloomShaderProgram>(api,"pbr_bloom");
        if(shader?.EnsureReady()!=true) throw new InvalidOperationException("Owned bloom shader unavailable.");
        return pipeline=draw.Prepare(shader,down[0].Target);
    }
    /// <summary>Extracts once, downsamples, then combines narrow and broad energy with normalized weights.</summary>
    internal void Render(GraphicsCommandContext commands,PostprocessDraw draw,GpuTexture scene,(GpuTexture? Texture,float ManualEV) exposure,PostprocessParameters settings)
    {
        for(int i=0;i<down.Count;i++) {
            shader!.SourceImage=i==0?scene:down[i-1].Texture;
            shader.SecondaryImage=exposure.Texture??scene;
            shader.Capture(new(0,0,i==0?0:1,exposure.Texture is not null?1:0),new(settings.BloomThreshold,settings.BloomKnee,settings.BloomStrength,exposure.ManualEV));
            draw.Submit(commands,pipeline!,down[i].Target);
        }
        for(int i=up.Count-1;i>=0;i--) {
            shader!.SourceImage=down[i].Texture;
            shader.SecondaryImage=i==up.Count-1?down[i+1].Texture:up[i+1].Texture;
            shader.Capture(new(0,0,2,0),Vector4.Zero);
            draw.Submit(commands,pipeline!,up[i].Target);
        }
    }
    /// <summary>Releases only bloom storage; the common draw owner retains executable PSOs.</summary>
    public void Dispose() { foreach(var image in up) image.Dispose(); foreach(var image in down) image.Dispose(); up.Clear(); down.Clear(); shader=null; pipeline=null; }
    #endregion
}
