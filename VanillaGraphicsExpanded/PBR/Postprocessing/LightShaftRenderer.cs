using System;
using System.Numerics;
using VanillaGraphicsExpanded.ModSystems;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Rendering.Pipeline;
using VanillaGraphicsExpanded.Rendering.Shaders;
using Vintagestory.API.Client;
namespace VanillaGraphicsExpanded.PBR.Postprocessing;
/// <summary>Owns independent shaft-bloom and distance-occlusion outputs with bounded radial filtering.</summary>
internal sealed class LightShaftRenderer : IDisposable
{
    private PostprocessImage? first,second,bloom,occlusion;
    private LightShaftShaderProgram? shader;
    private GraphicsPipeline? pipeline;
    private int passes,samples;
    #region Public API
    /// <summary>Publishes unexposed shaft bloom for additive final composition.</summary>
    internal DynamicTexture2D Texture=>bloom!.Texture;
    /// <summary>Publishes visibility for atmospheric scattering consumers, never whole-scene multiplication.</summary>
    internal DynamicTexture2D Occlusion=>occlusion!.Texture;
    /// <summary>Prepares persistent reduced-resolution resources under native quality and configured work limits.</summary>
    internal GraphicsPipeline Prepare(ICoreClientAPI api,PostprocessDraw draw,int width,int height,int quality=1,int sampleLimit=32,bool occlusionOnly=false)
    {
        int frameWidth=width,frameHeight=height;
        int divisor=quality>=2?2:4;
        width=Math.Max(1,(width+divisor-1)/divisor); height=Math.Max(1,(height+divisor-1)/divisor);
        if(first is null||first.Texture.Width!=width||first.Texture.Height!=height||occlusionOnly&&(occlusion is null||occlusion.Texture.Width!=frameWidth||occlusion.Texture.Height!=frameHeight))
        {
            Dispose();
            try {
                first=new(width,height,"Solar.RadialA"); second=new(width,height,"Solar.RadialB");
                if(occlusionOnly) occlusion=new(frameWidth,frameHeight,"Solar.Occlusion");
                else bloom=new(width,height,"Solar.Bloom");
            } catch { Dispose(); throw; }
        }
        passes=quality>=2?3:2;
        samples=Math.Clamp(Math.Min(sampleLimit,quality>=3?64:quality>=2?32:16),1,64);
        shader=GpuShaderPrograms.Get<LightShaftShaderProgram>(api,"pbr_lightshafts");
        if(shader?.EnsureReady()!=true) throw new InvalidOperationException("Owned solar-shaft shader unavailable.");
        return pipeline=draw.Prepare(shader,first.Target);
    }
    /// <summary>Extracts unexposed HDR glare, filters both independent signals, and publishes separate outputs.</summary>
    internal void Render(GraphicsCommandContext commands,PostprocessDraw draw,ICoreClientAPI api,float[] projection,
        GpuTexture scene,GpuTexture glow,GpuTexture depth,(GpuTexture? Texture,float ManualEV) exposure,PostprocessParameters settings,bool occlusionOnly=false)
    {
        var lighting=AtmosphereModSystem.Lighting??throw new InvalidOperationException("Atmospheric lighting is unavailable for solar shafts.");
        Span<float> transform=stackalloc float[16];
        MatrixHelper.Multiply(projection,api.Render.CameraMatrixOriginf,transform);
        Vector3 direction=lighting.Sun;
        float x=transform[0]*direction.X+transform[4]*direction.Y+transform[8]*direction.Z;
        float y=transform[1]*direction.X+transform[5]*direction.Y+transform[9]*direction.Z;
        float w=transform[3]*direction.X+transform[7]*direction.Y+transform[11]*direction.Z;
        Vector2 screen=w>1e-5f?new(x/w*.5f+.5f,y/w*.5f+.5f):new(-10);
        float edge=Math.Max(Math.Abs(screen.X-.5f),Math.Abs(screen.Y-.5f));
        float visibility=w>1e-5f?Math.Clamp((.65f-edge)/.15f,0,1):0;
        visibility*=Math.Clamp((direction.Y+.03f)/.06f,0,1)*(1-Math.Clamp(api.Render.ShaderUniforms.CameraUnderwater,0,1));
        // The scene already contains atmospheric solar extinction. Only normalize its tint here.
        Vector3 tint=Vector3.Max(Vector3.Zero,lighting.Solar);
        float peak=Math.Max(tint.X,Math.Max(tint.Y,tint.Z));
        tint=peak>1e-6f?tint/peak:Vector3.Zero;
        if(peak<=1e-6f) visibility=0;
        var sun=new Vector4(screen,visibility,(float)scene.Width/scene.Height);
        var solar=new Vector4(tint,128);
        shader!.SourceImage=scene; shader.VisibilityImage=glow; shader.DepthImage=depth;
        shader.ExposureImage=exposure.Texture??scene;
        shader.Capture(new(projection[10],projection[14],occlusionOnly?4:0,exposure.Texture is null?0:1),
            new(settings.BloomThreshold,settings.LightShaftLimit,settings.LightShaftStrength,exposure.ManualEV),sun,solar);
        draw.Submit(commands,pipeline!,first!.Target);
        // Ping-pong keeps every sampling source distinct from the active draw attachment.
        PostprocessImage source=first,destination=second!;
        for(int pass=0;pass<passes;pass++)
        {
            float step=.04f*MathF.Pow(3,pass+3-passes);
            shader.SourceImage=source.Texture;
            shader.Capture(new(0,0,1,samples),new(step,0,0,0),sun,solar);
            draw.Submit(commands,pipeline!,destination.Target);
            (source,destination)=(destination,source);
        }
        shader.SourceImage=source.Texture;
        shader.Capture(new(0,0,occlusionOnly?3:2,0),Vector4.Zero,sun,solar);
        draw.Submit(commands,pipeline!,occlusionOnly?occlusion!.Target:bloom!.Target);
    }
    /// <summary>Retires all shaft-only storage and publication together.</summary>
    public void Dispose()
    {
        first?.Dispose(); second?.Dispose(); bloom?.Dispose(); occlusion?.Dispose();
        first=second=bloom=occlusion=null; shader=null; pipeline=null;
    }
    #endregion
}
