using System.Collections.Immutable;
using System.Numerics;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.PBR;
using VanillaGraphicsExpanded.PBR.Atmosphere;
using VanillaGraphicsExpanded.PBR.Clouds;
using VanillaGraphicsExpanded.PBR.Clouds.LightingVolume;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Rendering.Integration;
using VanillaGraphicsExpanded.Rendering.Pipeline;
using VanillaGraphicsExpanded.Rendering.Pipeline.Passes;
using Vintagestory.API.MathTools;

namespace VanillaGraphicsExpanded.Tests.GPU.Fixtures;

/// <summary>Owns tiny native inputs and executes the candidate's actual support reduction and full-resolution OIT renderer.</summary>
internal sealed class LightingVolumeViewSceneFixture:IDisposable
{
    private readonly List<IDisposable> resources=[];
    private readonly DynamicTexture2D map,material,visibility,liquid,shafts;
    private readonly DynamicTexture3D aerial,attenuation;
    private readonly VgeFrameUniformBuffer camera;
    private readonly TestFrameLightingPublication lighting;
    private readonly LightingVolumeStorage storage;
    private readonly LightingVolumeRenderer renderer;
    private readonly CloudOitSurface output;
    private CloudMapInput input;
    internal AtmosphereLighting Atmosphere { get; set; }=new(Vector3.UnitY,new Vector3(30),new Vector3(10),
        Vector3.Zero,Vector3.Zero,ImmutableArray<float>.Empty){Moon=-Vector3.UnitY,Lunar=new Vector3(3),CalibrationGeneration=1,LookupStorageScale=1f/16};
    internal float Underwater { get; set; }
    internal Vector4 Fog { get; set; }
    internal Vector3 FogColor { get; set; }

    #region Public API
    #region Acquisition
    /// <summary>Allocates one-pixel scene resources using existing texture/framebuffer/program owners.</summary>
    internal LightingVolumeViewSceneFixture(ComponentShaderPrograms programs)
    {
        try
        {
            camera=Own(TestFrameCamera.CreateFromProjection(Projection(),1,1,.1f,1000));
            lighting=Own(new TestFrameLightingPublication());
            map=Own(DynamicTexture2D.Create(20,20,PixelInternalFormat.Rgba32f));
            material=Own(DynamicTexture2D.Create(1,1,PixelInternalFormat.R32f));
            visibility=Own(DynamicTexture2D.Create(1,1,PixelInternalFormat.R32f));
            liquid=Own(DynamicTexture2D.Create(1,1,PixelInternalFormat.R32f));
            shafts=Own(DynamicTexture2D.Create(1,1,PixelInternalFormat.R32f));shafts.UploadDataImmediate(new float[]{0});
            aerial=Own(DynamicTexture3D.Create(1,2,1,PixelInternalFormat.Rgba32f,textureTarget:TextureTarget.Texture3D));
            attenuation=Own(DynamicTexture3D.Create(1,1,1,PixelInternalFormat.Rgba32f,textureTarget:TextureTarget.Texture3D));
            SetAerial(0,0);SetDepths(100,100,100);
            input=new(new object(),1,1,1,1,1,0,new(map.TextureId,2,3,4,20,50,5),
                default(CloudPlacement) with {Offset=new Vector3(0,0,-50)});
            storage=Own(new LightingVolumeStorage(()=>LightingVolumeMaintenanceProgram.Create(programs.Api)));
            SetMap(1);storage.Ensure(input,LightingVolumeEncoding.Create(10000),()=>true);
            renderer=Own(new LightingVolumeRenderer(programs.Create<LightingVolumeViewProgram>(),
                ()=>LightingVolumeHeightProgram.Create(programs.Api)));
            output=Own(new CloudOitSurface(1,1,"Tests.Cloud.LightingVolume.View"));
        }
        catch {Dispose();throw;}
    }
    #endregion
    #region Authored inputs
    /// <summary>Authors finite native support, optionally a gap along the receiver's ray.</summary>
    internal void SetMap(float extinction,float lower=-.5f,float upper=.5f,bool gap=false)
    {
        var data=new float[20*20*4];
        for(int z=0;z<20;z++)for(int x=0;x<20;x++)
        {int i=(z*20+x)*4;data[i]=gap&&z==9?0:extinction;data[i+2]=lower;data[i+3]=upper;}
        map.UploadDataImmediate(data);
    }
    /// <summary>Authors independent material, visibility and liquid clips.</summary>
    internal void SetDepths(float opaque,float particles,float water)
    {material.UploadDataImmediate(new[]{Depth(opaque)});visibility.UploadDataImmediate(new[]{Depth(particles)});liquid.UploadDataImmediate(new[]{Depth(water)});}
    /// <summary>Moves the camera-relative cloud layer without moving the fixed lighting band.</summary>
    internal void SetLocalHeight(float height)=>input=input with {Placement=input.Placement with {Offset=new Vector3(0,-height,-50)}};
    /// <summary>Authors constant physical aerial scattering and dimensionless loss.</summary>
    internal void SetAerial(float scatter,float loss)
    {
        aerial.UploadDataImmediate(new[]{scatter/16,scatter/16,scatter/16,0,0,0,0,0},0,0,0,1,2,1);
        attenuation.UploadDataImmediate(new[]{loss,loss,loss,0},0,0,0,1,1,1);
    }
    /// <summary>Seeds exact masks and constant physical cached lighting through managed uploads.</summary>
    internal void SeedLighting(float solar,float lunar,Vector3 diffuse,int mask)
    {
        var d=storage.Domain!;var rg=new float[d.Voxels*2];var rgba=new float[d.Voxels*4];
        for(int z=1;z<d.Size-1;z++)for(int y=1;y<33;y++)for(int x=1;x<d.Size-1;x++)
        {
            int i=(z*34+y)*d.Size+x;rg[2*i]=solar;rg[2*i+1]=lunar;
            rgba[4*i]=diffuse.X/storage.Encoding.Scale;rgba[4*i+1]=diffuse.Y/storage.Encoding.Scale;
            rgba[4*i+2]=diffuse.Z/storage.Encoding.Scale;rgba[4*i+3]=mask;
        }
        storage.Direct.UploadDataImmediate(rg,0,0,0,d.Size,34,d.Size);storage.Diffuse.UploadDataImmediate(rgba,0,0,0,d.Size,34,d.Size);
    }
    #endregion
    #region Submission and retirement
    /// <summary>Clears native products and draws the actual candidate owner with a frozen test scene publication.</summary>
    internal float[][] Draw(LightingVolumeViewSettings settings,float scale=1,Func<bool>? current=null,bool succeeds=true,
        LightingVolumeSolution? solution=null)
    {
        if(solution is not null)
        {
            solution.Prepare(settings);solution.Bounds.Observe(Atmosphere);
            Span<Vector4> planes=stackalloc Vector4[6];LightingVolumeViewFrustum.Capture(camera,input,planes);
            Assert.True(solution.Update(input,map,Atmosphere,planes,settings,current??(()=>true)),solution.Failure);
        }
        lighting.Capture(scale);Assert.True(renderer.Prepare(settings),renderer.Failure);
        RenderPassColor[] colors=Enumerable.Range(0,6).Select(i=>new RenderPassColor(i,AttachmentLoad.Clear,
            Clear:i<2?ColorClearValue.Float(1,1,1,1):ColorClearValue.Float(0,0,0,0))).ToArray();
        Assert.True(GraphicsCommandContext.TryRun("Tests.Cloud.LightingVolume.Clear",[],true,commands=>
        {commands.BeginPass(new RenderPassDesc(output.Target,colors));commands.EndPass();}));
        var scene=new LightingVolumeViewScene(camera,lighting.Buffer,Atmosphere,material,visibility,liquid,aerial,attenuation,
            shafts,Underwater,Fog,FogColor);
        bool drawn=renderer.Draw(solution?.Storage??storage,input,map,scene,output.Target,current??(()=>true));
        if(succeeds) Assert.True(drawn,renderer.Failure);
        else {Assert.False(drawn);Assert.False(renderer.Ready);Assert.NotNull(renderer.Failure);}
        return output.Images.Select(texture=>texture.ReadPixels()).ToArray();
    }
    /// <summary>Releases owned GPU resources in reverse acquisition order, preserving borrowed fixture programs.</summary>
    public void Dispose()
    {
        RenderingCleanup.Run(resources.AsEnumerable().Reverse().Select(value=>(Action)value.Dispose).ToArray());resources.Clear();
    }
    #endregion
    #endregion
    #region Private
    /// <summary>Owns each acquired scene resource immediately so partial construction and reverse teardown remain safe.</summary>
    private T Own<T>(T value) where T:IDisposable {resources.Add(value);return value;}
    /// <summary>Defines an independently authored perspective projection for depth-clipping arithmetic.</summary>
    private static float[] Projection()=>[1,0,0,0,0,1,0,0,0,0,-1.00020002f,-1,0,0,-.200020002f,0];
    /// <summary>Encodes one authored view distance using the corresponding perspective coefficients.</summary>
    private static float Depth(float distance)=>(1.00020002f-.200020002f/distance)*.5f+.5f;
    #endregion
}
