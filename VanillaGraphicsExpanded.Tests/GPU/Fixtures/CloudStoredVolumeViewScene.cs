using System.Collections.Immutable;
using System.Numerics;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.PBR.Atmosphere;
using VanillaGraphicsExpanded.PBR.Clouds;
using VanillaGraphicsExpanded.PBR.Clouds.Volume;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Rendering.Integration;
using VanillaGraphicsExpanded.Rendering.Pipeline;
using VanillaGraphicsExpanded.Rendering.Pipeline.Passes;
using VolumeStorage=VanillaGraphicsExpanded.PBR.Clouds.Volume.CloudVolumeStorage;

namespace VanillaGraphicsExpanded.Tests.GPU.Fixtures;

/// <summary>Owns a tiny authored stored volume and independent native OIT/depth/atmosphere inputs.</summary>
internal sealed class CloudStoredVolumeViewScene:IDisposable
{
    private readonly List<IDisposable> resources=[];
    private readonly DynamicTexture2D map,material,visibility,liquid,shafts;
    private readonly DynamicTexture3D aerial,attenuation;
    private readonly VgeFrameUniformBuffer camera;
    private readonly TestFrameLightingPublication lighting;
    private readonly VolumeStorage storage;
    private readonly CloudVolumeViewRenderer renderer;
    private readonly CloudOitSurface output;
    private readonly CloudMapInput input;
    internal AtmosphereLighting Atmosphere {get;set;}=new(Vector3.UnitZ,new Vector3(30),new Vector3(10),Vector3.Zero,Vector3.Zero,
        ImmutableArray<float>.Empty){Moon=-Vector3.UnitY,Lunar=new Vector3(3),CalibrationGeneration=7,LookupStorageScale=1f/16};
    #region Public API
    /// <summary>Allocates only existing GPU owners and the actual stored-volume view program.</summary>
    internal CloudStoredVolumeViewScene(ComponentShaderPrograms programs)
    {
        try
        {
            camera=Own(TestFrameCamera.CreateFromProjection([1,0,0,0,0,1,0,0,0,0,-1.00020002f,-1,0,0,-.200020002f,0],1,1,.1f,1000));
            lighting=Own(new TestFrameLightingPublication());map=Own(DynamicTexture2D.Create(20,20,PixelInternalFormat.Rgba32f));
            material=Own(DynamicTexture2D.Create(1,1,PixelInternalFormat.R32f));visibility=Own(DynamicTexture2D.Create(1,1,PixelInternalFormat.R32f));
            liquid=Own(DynamicTexture2D.Create(1,1,PixelInternalFormat.R32f));shafts=Own(DynamicTexture2D.Create(1,1,PixelInternalFormat.R32f));
            shafts.UploadDataImmediate(new float[]{0});
            aerial=Own(DynamicTexture3D.Create(1,2,1,PixelInternalFormat.Rgba32f,textureTarget:TextureTarget.Texture3D));
            attenuation=Own(DynamicTexture3D.Create(1,1,1,PixelInternalFormat.Rgba32f,textureTarget:TextureTarget.Texture3D));
            aerial.UploadDataImmediate(new float[8],0,0,0,1,2,1);attenuation.UploadDataImmediate(new float[4],0,0,0,1,1,1);
            SetDepths(100,100,100);
            var native=new float[20*20*4];for(int i=0;i<400;i++){native[i*4]=1;native[i*4+2]=-.5f;native[i*4+3]=.5f;}map.UploadDataImmediate(native);
            input=new(new object(),1,1,1,1,1,0,new(map.TextureId,2,3,4,20,50,5),default(CloudPlacement) with{Offset=new Vector3(0,0,-50)});
            storage=Own(new VolumeStorage(()=>CloudVolumeMaintenanceProgram.Create(programs.Api),()=>CloudVolumeCopyProgram.Create(programs.Api)));
            var domain=new CloudVolumeDomain(20,input.Placement,-100,100);storage.Request(domain,new(input.Owner,1,1),new(new Vector3(1000)),new Vector3(12),()=>true);
            while(!storage.InitializeNext(()=>true)){}Assert.True(storage.PublishPending(()=>true,()=>true));
            var fields=storage.Published;var rg=new float[domain.Voxels*2];
            for(int i=0;i<domain.Voxels;i++){rg[i*2]=.25f;rg[i*2+1]=.75f;}fields.Direct.UploadDataImmediate(rg,0,0,0,domain.Size,66,domain.Size);
            SeedDensity(.0078125f);
            renderer=Own(new CloudVolumeViewRenderer(programs.Create<CloudVolumeViewProgram>()));output=Own(new CloudOitSurface(1,1,"Tests.Cloud.StoredVolume.View"));
        }
        catch{Dispose();throw;}
    }
    /// <summary>Authors authoritative density and a matching conservative occupancy field.</summary>
    internal void SeedDensity(float sigma)
    {
        var fields=storage.Published;var d=fields.Domain;
        fields.Density.UploadDataImmediate(Enumerable.Repeat(sigma,d.Voxels).ToArray(),0,0,0,d.Size,66,d.Size);
        fields.Occupancy.UploadData(Enumerable.Repeat(sigma>0?1u:0u,d.Blocks).ToArray());
    }
    /// <summary>Authors independent material, particle and liquid depth clips in projection space.</summary>
    internal void SetDepths(float opaque,float particles,float water)
    {
        material.UploadDataImmediate(new[]{Depth(opaque)});visibility.UploadDataImmediate(new[]{Depth(particles)});liquid.UploadDataImmediate(new[]{Depth(water)});
    }
    /// <summary>Draws actual typed OIT, optionally advancing the complete independent stored-volume update chain.</summary>
    internal float[][] Draw(CloudVolumeViewSettings settings,float scale=1,CloudVolumeSolution? solution=null,bool succeeds=true)
    {
        if(solution is not null)
        {
            solution.Prepare(settings);solution.Admission.Observe(Atmosphere);
            Assert.True(solution.Update(input,map,Atmosphere,camera,settings,()=>true),solution.Failure);
        }
        lighting.Capture(scale);Assert.True(renderer.Prepare(settings),renderer.Failure);
        RenderPassColor[] colors=Enumerable.Range(0,6).Select(i=>new RenderPassColor(i,AttachmentLoad.Clear,
            Clear:i<2?ColorClearValue.Float(1,1,1,1):ColorClearValue.Float(0,0,0,0))).ToArray();
        Assert.True(GraphicsCommandContext.TryRun("Tests.Cloud.StoredVolume.Clear",[],true,commands=>
            {commands.BeginPass(new RenderPassDesc(output.Target,colors));commands.EndPass();}));
        var scene=new CloudVolumeViewScene(camera,lighting.Buffer,Atmosphere,material,visibility,liquid,aerial,attenuation,shafts);
        bool drawn=renderer.Draw(solution?.Storage??storage,input,scene,output.Target,()=>true);
        if(succeeds)Assert.True(drawn,renderer.Failure);else{Assert.False(drawn);Assert.NotNull(renderer.Failure);}
        return output.Images.Select(texture=>texture.ReadPixels()).ToArray();
    }
    /// <summary>Releases acquisitions in reverse order through existing cleanup semantics.</summary>
    public void Dispose(){RenderingCleanup.Run(resources.AsEnumerable().Reverse().Select(value=>(Action)value.Dispose).ToArray());resources.Clear();}
    #endregion
    #region Private
    /// <summary>Adopts resources immediately so partial constructor failure cannot leak an allocation.</summary>
    private T Own<T>(T value)where T:IDisposable{resources.Add(value);return value;}
    /// <summary>Encodes one finite camera distance using the fixture's projection coefficients.</summary>
    private static float Depth(float distance)=>(1.00020002f-.200020002f/distance)*.5f+.5f;
    #endregion
}
