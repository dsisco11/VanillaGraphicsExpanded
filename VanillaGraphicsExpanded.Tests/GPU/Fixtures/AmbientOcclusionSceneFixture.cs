using VanillaGraphicsExpanded.Rendering.RHI;
using System.Diagnostics;
using System.Numerics;
using HarmonyLib;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.PBR.Postprocessing;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Rendering.Pipeline;
using VanillaGraphicsExpanded.Tests.GPU.Helpers;

namespace VanillaGraphicsExpanded.Tests.GPU.Fixtures;

/// <summary>Owns an analytically projected AO workload, its input textures and the shared hierarchy for component validation.</summary>
internal sealed class AmbientOcclusionSceneFixture : IDisposable
{
    private readonly ShaderTestFramework framework;
    private readonly PostSsaoShaderProgram horizon;
    private readonly AmbientOcclusionFilterShaderProgram filter;
    private readonly MinimumChildMaskShaderProgram maskShader;
    private readonly MinimumChildMaskPass comparisonMasks=new();
    private readonly DepthHierarchyComputeShader compute;
    private readonly DepthHierarchyPass hierarchy=new();
    private readonly DynamicTexture2D normal,material,environment;
    private readonly float[] materialData;
    private readonly float originalRoughness;
    private readonly float availability;

    #region Public API
    internal int Width { get; }
    internal int Height { get; }
    internal DynamicTexture2D Depth { get; }
    internal DynamicTexture2D Albedo { get; }
    internal DynamicTexture2D Position { get; }
    internal Texture3D Surface { get; }
    internal VgeFrameUniformBuffer Camera { get; }
    /// <summary>Derives fixture reporting bytes from the known hierarchy geometry without production instrumentation.</summary>
    internal long HierarchyBytes => hierarchy.Texture is { } texture
        ? Enumerable.Range(0, texture.MipLevels).Sum(level => 4L * Math.Max(1, texture.Width >> level) * Math.Max(1, texture.Height >> level))
        : 0;

    #region Input ownership
    /// <summary>Projects planes and exact corner intersections using the same camera later borrowed by AO and lighting.</summary>
    internal AmbientOcclusionSceneFixture(ShaderTestFramework framework,ComponentShaderPrograms programs,int width,int height,int scene,
        float receiverDistance=3,float verticalFov=90,float edgeFraction=.5f,float roughness=.5f,float metallic=0,float availability=.5f,
        float occluderTransmission=0,float receiverTransmission=0)
    {
        this.framework=framework;Width=width;Height=height;originalRoughness=roughness;this.availability=availability;
        horizon=programs.Create<PostSsaoShaderProgram>();filter=programs.Create<AmbientOcclusionFilterShaderProgram>();compute=programs.CreateDepthHierarchy();
        maskShader=programs.Create<MinimumChildMaskShaderProgram>();
        float aspect=(float)width/height,tanHalf=MathF.Tan(verticalFov*MathF.PI/360);
        const float near=.1f,far=100f;
        float a=-(far+near)/(far-near),b=-2*far*near/(far-near);
        float[] inverse=[aspect*tanHalf,0,0,0, 0,tanHalf,0,0, 0,0,0,1/b, 0,0,-1,a/b];
        float[] view=[1,0,0,0, 0,1,0,0, 0,0,1,0, 0,0,0,1];
        Camera=TestFrameCamera.Create(inverse,view,width,height,near,far);
        float[] depths=new float[width*height],normals=new float[width*height*4],colors=new float[width*height*4],positions=new float[width*height*4];
        materialData=new float[width*height*4];
        int edge=(int)(width*edgeFraction);
        // Intersect authored view rays with z planes, the corner at x=.5 or a ground plane,
        // then project the real point into hardware depth. Ground rays above the horizon are sky.
        for(int y=0;y<height;y++) for(int x=0;x<width;x++) {
            int i=y*width+x;
            float rayX=((x+.5f)/width*2-1)*aspect*tanHalf,rayY=((y+.5f)/height*2-1)*tanHalf;
            bool front=((scene==4||scene==8)&&x<edge)||(scene==9&&x>=edge-2&&x<edge);
            float z=scene==7?97:front?receiverDistance-.3f:scene==1?receiverDistance/(1-.2f*rayX):receiverDistance;
            if(scene==10) z=rayY<0?1.6f/-rayY:far;
            bool sideWall=scene==5&&rayX>0&&.5f/rayX<z;
            if(sideWall)z=.5f/rayX;
            depths[i]=scene==2||(scene==6&&x%4<2)||(scene==10&&z>=far)?1:.5f*(-a+b/z)+.5f;
            Vector3 n=scene==10?Vector3.UnitY:sideWall?-Vector3.UnitX:scene==1?Vector3.Normalize(new(.2f,0,1)):Vector3.UnitZ;
            normals[i*4]=n.X*.5f+.5f;normals[i*4+1]=n.Y*.5f+.5f;normals[i*4+2]=n.Z*.5f+.5f;
            normals[i*4+3]=scene==3||(scene==8&&front)?-1:1;
            materialData[i*4]=roughness;materialData[i*4+1]=metallic;
            materialData[i*4+3]=front?occluderTransmission:receiverTransmission;
            colors[i*4]=colors[i*4+1]=colors[i*4+2]=.5f;colors[i*4+3]=1;
            positions[i*4]=rayX*z;positions[i*4+1]=rayY*z;positions[i*4+2]=-z;positions[i*4+3]=1;
        }
        Depth=framework.CreateTexture(width,height,PixelInternalFormat.R32f,depths);
        normal=framework.CreateTexture(width,height,PixelInternalFormat.Rgba32f,normals);
        material=framework.CreateTexture(width,height,PixelInternalFormat.Rgba32f,materialData);
        environment=framework.CreateTexture(width,height,PixelInternalFormat.Rgba32f,EnvironmentData(availability));
        Albedo=framework.CreateTexture(width,height,PixelInternalFormat.Rgba32f,colors);
        Position=framework.CreateTexture(width,height,PixelInternalFormat.Rgba32f,positions);
        Surface=LayeredTestTexture.Create(normal,material,environment);
        hierarchy.Prepare(width,height,compute);
    }

    #endregion

    #region Submission and diagnostics
    /// <summary>Runs the actual hierarchy, horizon, denoising and full-resolution reconstruction in their restoring boundaries.</summary>
    internal void Render(AmbientOcclusionPass owner,PostprocessDraw draw,int quality)
    {
        var pipelines=owner.Prepare(draw,Width,Height,quality,horizon,filter,maskShader);
        hierarchy.Render(Depth);
        {
    var commands=RHICommandContext.Current;
    using var scope31=commands.EnterBoundary(new("Tests.AmbientOcclusionScene",true,graphics:System.Linq.Enumerable.Select<RHIGraphicsPipeline,RHIGraphicsPipelineRef>(pipelines,value=>value),operations:BoundaryOperations.RenderPasses));
    scope31.Run(()=>owner.Render(commands,draw,Depth,Surface,hierarchy.Texture!,Camera));
}
        Assert.Equal(ErrorCode.NoError,GL.GetError());
    }

    /// <summary>Reads each production-owned stage without adding a diagnostic API or altering production submission.</summary>
    internal static (float[] Raw,float[] Filtered,float[] Reconstructed) ReadStages(AmbientOcclusionPass owner)
    {
        var raw=Assert.IsType<DynamicTexture2D>(AccessTools.Field(typeof(AmbientOcclusionPass),"raw").GetValue(owner));
        var filtered=Assert.IsType<DynamicTexture2D>(AccessTools.Field(typeof(AmbientOcclusionPass),"filtered").GetValue(owner));
        var output=Assert.IsType<DynamicTexture2D>(owner.Texture);
        return(raw.ReadPixels(),filtered.ReadPixels(),output.ReadPixels());
    }

    /// <summary>Prepares matched horizon-only submissions sharing one completed hierarchy and unchanged scene inputs.</summary>
    internal (Action Reference, Action Current) PrepareHorizonComparison(AoHorizonReferenceProgram reference,
        PostprocessDraw draw, GpuFramebuffer referenceTarget, GpuFramebuffer currentTarget, int nativeQuality)
    {
        hierarchy.Render(Depth);
        var maskPipeline=comparisonMasks.Prepare(draw,Width,Height,maskShader);
        {
    var commands=RHICommandContext.Current;
    using var scope32=commands.EnterBoundary(new("Tests.AoComparisonMasks",true,graphics:System.Linq.Enumerable.Select<RHIGraphicsPipeline,RHIGraphicsPipelineRef>([maskPipeline],value=>value),operations:BoundaryOperations.RenderPasses));
    scope32.Run(()=>comparisonMasks.Render(commands,draw,hierarchy.Texture!));
}
        reference.Capture(Depth, Surface, hierarchy.Texture!, Camera, nativeQuality);
        var quality = AmbientOcclusionQuality.FromNative(nativeQuality);
        horizon.DepthImage = Depth; horizon.SurfaceImage = Surface; horizon.DepthHierarchy = hierarchy.Texture;
        horizon.MinimumChildren=comparisonMasks.Texture;
        horizon.Capture(Camera, new(0, 0, quality.Divisor, 0),
            new(1.25f, .25f, quality.Directions, quality.Steps), new(64, 96, 0, 0));
        var referencePipeline = draw.Prepare(reference,referenceTarget.PostprocessPass());
        var currentPipeline = draw.Prepare(horizon,currentTarget.PostprocessPass());
        // Preparation, hierarchy/mask generation and descriptor mutation are excluded from horizon-only timing.
        return (
            () => {
    var commands=RHICommandContext.Current;
    using var scope33=commands.EnterBoundary(new("Tests.AoReference",true,graphics:System.Linq.Enumerable.Select<RHIGraphicsPipeline,RHIGraphicsPipelineRef>([referencePipeline],value=>value),operations:BoundaryOperations.RenderPasses));
    scope33.Run(()=>draw.Submit(commands, referencePipeline,referenceTarget.PostprocessPass(), reference.CaptureBindings()));
},
            () => {
    var commands=RHICommandContext.Current;
    using var scope34=commands.EnterBoundary(new("Tests.AoCurrent",true,graphics:System.Linq.Enumerable.Select<RHIGraphicsPipeline,RHIGraphicsPipelineRef>([currentPipeline],value=>value),operations:BoundaryOperations.RenderPasses));
    scope34.Run(()=>draw.Submit(commands, currentPipeline,currentTarget.PostprocessPass(), horizon.CaptureBindings()));
});
    }

    /// <summary>Measures matched, prepared GPU work and CPU submission, excluding target creation, readback and query waits.</summary>
    internal (double[] Gpu,double[] Cpu) Measure(AmbientOcclusionPass owner,PostprocessDraw draw,int quality,int count=5)
    {
        Render(owner,draw,quality);
        var pipelines=owner.Prepare(draw,Width,Height,quality,horizon,filter,maskShader);
        double[] gpu=new double[count],cpu=new double[count];
        for(int sample=0;sample<count;sample++) {
            using var timer=GpuTimerQuery.Create();
            long start=Stopwatch.GetTimestamp();timer.Begin();
            hierarchy.Render(Depth);
            {
    var commands=RHICommandContext.Current;
    using var scope35=commands.EnterBoundary(new("Tests.AmbientOcclusionMeasurement",true,graphics:System.Linq.Enumerable.Select<RHIGraphicsPipeline,RHIGraphicsPipelineRef>(pipelines,value=>value),operations:BoundaryOperations.RenderPasses));
    scope35.Run(()=>owner.Render(commands,draw,Depth,Surface,hierarchy.Texture!,Camera));
}
            timer.End();cpu[sample]=Stopwatch.GetElapsedTime(start).TotalMilliseconds;
            gpu[sample]=timer.GetResultNanoseconds()/1e6;
        }
        return(gpu,cpu);
    }

    /// <summary>Controls environmental availability without changing geometry or propagated direct-sun visibility.</summary>
    internal void SetEnvironment(bool enabled)
    {
        Surface.UploadDataImmediate(EnvironmentData(enabled?availability:0),0,0,2,Width,Height,1);
    }

    /// <summary>Removes only the composite specular lobe for an independent diffuse contribution measurement.</summary>
    internal void SetCompositeRoughness(bool diffuseOnly)
    {
        for(int i=0;i<Width*Height;i++)materialData[i*4]=diffuseOnly?1:originalRoughness;
        Surface.UploadDataImmediate(materialData,0,0,1,Width,Height,1);
    }

    #endregion

    #region Lifetime
    /// <summary>Retires dependent hierarchy views and layered inputs before their source storage.</summary>
    public void Dispose()
    {
        comparisonMasks.Dispose();hierarchy.Dispose();Surface.Dispose();Camera.Dispose();Position.Dispose();Albedo.Dispose();
        environment.Dispose();material.Dispose();normal.Dispose();Depth.Dispose();
    }
    #endregion
    #endregion

    #region Private
    /// <summary>Authors constant indirect irradiance with independent, fully exposed direct-sun propagation.</summary>
    private float[] EnvironmentData(float value)
    {
        float[] data=new float[Width*Height*4];
        for(int i=0;i<Width*Height;i++){data[i*4]=data[i*4+1]=data[i*4+2]=value;data[i*4+3]=1;}
        return data;
    }
    #endregion
}
