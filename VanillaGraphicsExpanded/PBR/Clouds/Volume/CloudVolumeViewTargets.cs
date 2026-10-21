using System;
using System.Linq;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Rendering.Integration;
using VanillaGraphicsExpanded.Rendering.Pipeline.Passes;

namespace VanillaGraphicsExpanded.PBR.Clouds.Volume;

/// <summary>Owns compact transport, required observation age and independently optional diagnostic images.</summary>
internal sealed class CloudVolumeViewTargets : IDisposable
{
    internal DynamicTexture2D[] Images {get;private set;}
    internal GpuFramebuffer Target {get;private set;}
    internal static readonly RenderPassColor[] Outputs=Enumerable.Range(0,3).Select(i=>new RenderPassColor(i,
        AttachmentLoad.Clear,Clear:i==1?ColorClearValue.Float(1,1,1,1):ColorClearValue.Float(0,0,0,0))).ToArray();
    internal static readonly RenderPassColor[] HistoryOutputs=[..Outputs,new RenderPassColor(3,
        AttachmentLoad.Clear,Clear:ColorClearValue.Float(0,0,0,0))];
    private static readonly RenderPassColor[] DiagnosticOutputs=[..HistoryOutputs,new RenderPassColor(4,
        AttachmentLoad.Clear,Clear:ColorClearValue.Float(0,0,0,0))];
    /// <summary>Matches the selected executable's ordinary or diagnostic fragment outputs.</summary>
    internal RenderPassColor[] FilterOutputs=>Images.Length==5?DiagnosticOutputs:HistoryOutputs;
    /// <summary>Returns the optional image only while diagnostic capture owns an attachment.</summary>
    internal DynamicTexture2D? Diagnostics=>Images.Length==5?Images[4]:null;
    internal long Bytes=>(long)Target.Width*Target.Height*(Images.Length==5?54:Images.Length==4?46:44);
    #region Public API
    /// <summary>Allocates the selected ceil-divided target transactionally, preserving float32 ranges.</summary>
    internal CloudVolumeViewTargets(int width,int height,int divisor,bool history=false)
    {
        if(divisor is not (2 or 4 or 8))throw new ArgumentOutOfRangeException(nameof(divisor));
        Images=new DynamicTexture2D[history?4:3];
        PixelInternalFormat[] formats=[PixelInternalFormat.Rgba32f,PixelInternalFormat.Rgb32f,PixelInternalFormat.Rgba32f,PixelInternalFormat.R16f];
        try
        {
            for(int i=0;i<Images.Length;i++)Images[i]=DynamicTexture2D.Create((width+divisor-1)/divisor,(height+divisor-1)/divisor,formats[i],debugName:$"Cloud.Volume.View.{i}");
            Target=DynamicGpuFramebuffer.CreateMRT(Images,debugName:"Cloud.Volume.View.Reduced")
                ??throw new InvalidOperationException("Stored cloud intermediate framebuffer unavailable.");
        }
        catch{foreach(var image in Images)image?.Dispose();throw;}
    }
    /// <summary>Adds or retires the optional diagnostic attachment without replacing physical history or observation age.</summary>
    internal void ConfigureDiagnostics(bool enabled)
    {
        if(Images.Length<4)throw new InvalidOperationException("Raw cloud traces have no temporal diagnostics.");
        if((Diagnostics is not null)==enabled)return;
        DynamicTexture2D? added=null;
        GpuFramebuffer replacement;
        DynamicTexture2D[] next;
        try
        {
            // Build the replacement before retiring the current target. Failed allocation leaves
            // every physical image and its framebuffer usable under the existing failure boundary.
            if(enabled)
            {
                added=DynamicTexture2D.Create(Target.Width,Target.Height,PixelInternalFormat.Rgba16f,debugName:"Cloud.Volume.View.Diagnostics");
                next=[..Images,added];
            }
            else next=Images[..4];
            replacement=DynamicGpuFramebuffer.CreateMRT(next,debugName:"Cloud.Volume.View.Reduced")
                ??throw new InvalidOperationException("Stored cloud diagnostic framebuffer unavailable.");
        }
        catch{added?.Dispose();throw;}
        var oldTarget=Target;var removed=enabled?null:Diagnostics;
        Target=replacement;Images=next;
        RenderingCleanup.Run(oldTarget.Dispose,()=>removed?.Dispose());
    }
    /// <summary>Releases framebuffer metadata before its attachment images.</summary>
    public void Dispose()=>RenderingCleanup.Run(()=>Target.Dispose(),()=>RenderingCleanup.Run(Images.Select(image=>(Action)image.Dispose).ToArray()));
    #endregion
}
