using System;
using System.Numerics;
using VanillaGraphicsExpanded.ModSystems;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Rendering.Pipeline;
using VanillaGraphicsExpanded.Rendering.Shaders;
using Vintagestory.API.Client;
namespace VanillaGraphicsExpanded.PBR.Postprocessing;
/// <summary>Owns bounded screen-space solar visibility shafts without sampling HDR scene color.</summary>
internal sealed class GodRayRenderer : IDisposable
{
    private PostprocessImage? image;
    private GodRayShaderProgram? shader;
    private GraphicsPipeline? pipeline;
    #region Public API
    /// <summary>Publishes the half-resolution linear glare image.</summary>
    internal DynamicTexture2D Texture=>image!.Texture;
    /// <summary>Prepares half-resolution resources and the owned effect executable.</summary>
    internal GraphicsPipeline Prepare(ICoreClientAPI api,PostprocessDraw draw,int width,int height)
    {
        width=Math.Max(1,(width+1)/2); height=Math.Max(1,(height+1)/2);
        if(image is null||image.Texture.Width!=width||image.Texture.Height!=height) { Dispose(); image=new(width,height,"Solar.Shafts"); }
        shader=GpuShaderPrograms.Get<GodRayShaderProgram>(api,"pbr_godrays");
        if(shader?.EnsureReady()!=true) throw new InvalidOperationException("Owned solar-shaft shader unavailable.");
        return pipeline=draw.Prepare(shader,image.Target);
    }
    /// <summary>Projects the atmosphere's sun and attenuates the bounded source for horizon, screen edge and underwater views.</summary>
    internal void Render(GraphicsCommandContext commands,PostprocessDraw draw,ICoreClientAPI api,float[] projection,GpuTexture glow,GpuTexture depth,PostprocessParameters settings)
    {
        var lighting=AtmosphereModSystem.Lighting??throw new InvalidOperationException("Atmospheric lighting is unavailable for solar shafts.");
        Span<float> transform=stackalloc float[16];
        MatrixHelper.Multiply(projection,api.Render.CameraMatrixOriginf,transform);
        Vector3 sun=lighting.Sun;
        float x=transform[0]*sun.X+transform[4]*sun.Y+transform[8]*sun.Z;
        float y=transform[1]*sun.X+transform[5]*sun.Y+transform[9]*sun.Z;
        float w=transform[3]*sun.X+transform[7]*sun.Y+transform[11]*sun.Z;
        Vector2 screen=w>1e-5f?new(x/w*.5f+.5f,y/w*.5f+.5f):new(-10);
        float edge=Math.Max(Math.Abs(screen.X-.5f),Math.Abs(screen.Y-.5f));
        float visibility=w>1e-5f?Math.Clamp((.65f-edge)/.15f,0,1):0;
        visibility*=Math.Clamp((sun.Y+.03f)/.06f,0,1)*(1-Math.Clamp(api.Render.ShaderUniforms.CameraUnderwater,0,1));
        Vector3 radiance=Vector3.Max(Vector3.Zero,lighting.Solar)*settings.GodRayStrength;
        float peak=Math.Max(radiance.X,Math.Max(radiance.Y,radiance.Z));
        radiance*=Math.Min(1,settings.GodRayLimit/Math.Max(peak,1e-6f));
        shader!.VisibilityImage=glow; shader.DepthImage=depth;
        shader.Capture(Vector4.Zero,new(settings.GodRaySamples,1,0,0),new(screen,visibility,0),new(radiance,0));
        draw.Submit(commands,pipeline!,image!.Target);
    }
    /// <summary>Retires shaft-only resources when disabled or invalidated.</summary>
    public void Dispose() { image?.Dispose(); image=null; shader=null; pipeline=null; }
    #endregion
}
