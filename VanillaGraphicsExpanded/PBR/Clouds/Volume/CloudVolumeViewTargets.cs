using System;
using System.Linq;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Rendering.Integration;
using VanillaGraphicsExpanded.Rendering.Pipeline.Passes;

namespace VanillaGraphicsExpanded.PBR.Clouds.Volume;

/// <summary>Owns compact cloud radiance/opacity, bucket transmission and exact depth/completion metadata.</summary>
internal sealed class CloudVolumeViewTargets : IDisposable
{
    internal readonly DynamicTexture2D[] Images=new DynamicTexture2D[3];
    internal readonly GpuFramebuffer Target;
    internal static readonly RenderPassColor[] Outputs=Enumerable.Range(0,3).Select(i=>new RenderPassColor(i,
        AttachmentLoad.Clear,Clear:i==1?ColorClearValue.Float(1,1,1,1):ColorClearValue.Float(0,0,0,0))).ToArray();
    internal long Bytes=>(long)Target.Width*Target.Height*36;
    #region Public API
    /// <summary>Allocates the selected ceil-divided target transactionally, preserving float32 ranges.</summary>
    internal CloudVolumeViewTargets(int width,int height,int divisor)
    {
        if(divisor is not (2 or 4))throw new ArgumentOutOfRangeException(nameof(divisor));
        PixelInternalFormat[] formats=[PixelInternalFormat.Rgba32f,PixelInternalFormat.Rgb32f,PixelInternalFormat.Rg32f];
        try
        {
            for(int i=0;i<3;i++)Images[i]=DynamicTexture2D.Create((width+divisor-1)/divisor,(height+divisor-1)/divisor,formats[i],debugName:$"Cloud.Volume.View.{i}");
            Target=DynamicGpuFramebuffer.CreateMRT(Images,debugName:"Cloud.Volume.View.Reduced")
                ??throw new InvalidOperationException("Stored cloud intermediate framebuffer unavailable.");
        }
        catch{foreach(var image in Images)image?.Dispose();throw;}
    }
    /// <summary>Releases framebuffer metadata before its attachment images.</summary>
    public void Dispose()=>RenderingCleanup.Run(()=>Target.Dispose(),()=>RenderingCleanup.Run(Images.Select(image=>(Action)image.Dispose).ToArray()));
    #endregion
}
