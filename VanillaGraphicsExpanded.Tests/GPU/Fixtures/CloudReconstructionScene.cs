using System.Collections.Immutable;
using System.Numerics;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.PBR;
using VanillaGraphicsExpanded.PBR.Atmosphere;
using VanillaGraphicsExpanded.PBR.Clouds;
using VanillaGraphicsExpanded.PBR.Liquids;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Rendering.Pipeline;
using VanillaGraphicsExpanded.Rendering.Pipeline.Descriptions;
using VanillaGraphicsExpanded.Rendering.Pipeline.Passes;
using VanillaGraphicsExpanded.Rendering.Shaders;
using Vintagestory.API.MathTools;

namespace VanillaGraphicsExpanded.Tests.GPU.Fixtures;

/// <summary>Owns synthetic native inputs and executes the same three pass structure as the runtime.</summary>
internal sealed class CloudReconstructionScene : IDisposable
{
    private readonly int width,height,lowWidth,lowHeight;
    private readonly CloudVolumeProgram reference;
    private readonly CloudTraceProgram trace;
    private readonly CloudTemporalProgram temporal;
    private readonly CloudResolveProgram resolve;
    private readonly List<IDisposable> owned=[];
    private readonly VgeFrameUniformBuffer camera;
    private readonly TestFrameLightingPublication storage;
    private readonly DynamicTexture2D map,opaque,visibility,liquid;
    private readonly DynamicTexture3D aerial,loss;
    private readonly CloudVolumeSurface traced,historyA,historyB;
    private readonly CloudOitSurface result,expected;
    private readonly GraphicsPipelineLifetime lifetime;
    private readonly Dictionary<(GpuProgram Program,int Target,bool Oit,float Depth),(RenderPassDesc Pass,GraphicsPipeline Pipeline)> prepared=[];
    private List<(RenderPassDesc Pass,GraphicsPipeline Pipeline)>? queuedPasses;
    private readonly ArrayGraphicsGeometry geometry;
    private readonly float[] projection=[4,0,0,0,0,4,0,0,0,0,-1.00020002f,-1,0,0,-.200020002f,0];
    private readonly float[] inverse=new float[16];
    private CloudVolumeSurface? last;
    private float shift,lastShift;
    private readonly float[] previousProjection=new float[16],currentProjection=new float[16];
    private float surfaceDepth;
    private bool detail,underwater;
    private int referenceSamples=4;
    private AtmosphereLighting? capturedLighting;
    private CloudPlacement capturedPlacement;
    #region Public API
    #region View preparation
    /// <summary>Allocates small deterministic targets and independently authored column/depth/atmospheric fixtures.</summary>
    internal CloudReconstructionScene(int width,int height,CloudVolumeProgram reference,CloudTraceProgram trace,CloudTemporalProgram temporal,CloudResolveProgram resolve,bool mipmappedOpaque,int nativeMapWidth,bool independentStorage=false)
    {
        this.width=width;this.height=height;this.reference=reference;this.trace=trace;this.temporal=temporal;this.resolve=resolve;
        (lowWidth,lowHeight)=CloudVolumeStorage.Dimensions(width,height);
        camera=Own(new VgeFrameUniformBuffer()); storage=Own(new TestFrameLightingPublication());
        map=Own(DynamicTexture2D.Create(nativeMapWidth,nativeMapWidth,PixelInternalFormat.Rgba32f));
        int levels=1;for(int extent=Math.Max(width,height);extent>1;extent>>=1)levels++;
        opaque=Own(mipmappedOpaque?DynamicTexture2D.CreateMipmapped(width,height,PixelInternalFormat.R32f,levels):
            DynamicTexture2D.Create(width,height,PixelInternalFormat.R32f));
        visibility=Own(DynamicTexture2D.Create(width,height,PixelInternalFormat.R32f));
        liquid=Own(DynamicTexture2D.Create(width,height,PixelInternalFormat.R32f));
        aerial=Own(DynamicTexture3D.Create(1,2,1,PixelInternalFormat.Rgba32f,textureTarget:TextureTarget.Texture3D));
        loss=Own(DynamicTexture3D.Create(1,1,1,PixelInternalFormat.Rgba32f,textureTarget:TextureTarget.Texture3D));
        aerial.UploadDataImmediate(new float[8],0,0,0,1,2,1,0);loss.UploadDataImmediate(new float[4],0,0,0,1,1,1,0);
        if(independentStorage)
        {
            // Only storage sharing differs; both fixtures execute the current shader algorithm.
            traced=CreateIndependentBlock("Tests.Cloud.IndependentTrace");
            historyA=CreateIndependentBlock("Tests.Cloud.IndependentA");historyB=CreateIndependentBlock("Tests.Cloud.IndependentB");
        }
        else
        {
            var payloads=Own(new CloudVolumeStorage(lowWidth,lowHeight,"Tests.Cloud.Payloads"));
            traced=payloads.Blocks[0];historyA=payloads.Blocks[1];historyB=payloads.Blocks[2];
        }
        result=Own(new CloudOitSurface(width,height,"Tests.Cloud.Default"));expected=Own(new CloudOitSurface(width,height,"Tests.Cloud.Reference"));
        lifetime=Own(new GraphicsPipelineLifetime());
        geometry=Own(new ArrayGraphicsGeometry(CloudVolumeRenderer.Layout,PrimitiveType.Triangles,new Dictionary<int,GpuVbo>(),proceduralVertices:3));
        Mat4f.Invert(inverse,projection);
        reference.Frame=camera;trace.Frame=camera;temporal.Frame=camera;resolve.Frame=camera;
        reference.Lighting=storage.Buffer;trace.Lighting=storage.Buffer;resolve.Lighting=storage.Buffer;
        trace.PmjJitter=Own(CloudPmjJitterTexture.Create());
    }
    /// <summary>Seeds adversarial coarse levels and enables mip selection so an accidental implicit LOD becomes observable.</summary>
    internal void SetOpaqueCoarseDepth(float depth)
    {
        using var binding=StateCache.Current.BindTextureScope(TextureTarget.Texture2D,0,opaque.TextureId);
        GL.TexParameter(TextureTarget.Texture2D,TextureParameterName.TextureMinFilter,(int)TextureMinFilter.NearestMipmapNearest);
        for(int level=1;level<opaque.MipLevels;level++)
        {
            int mipWidth=Math.Max(1,width>>level),mipHeight=Math.Max(1,height>>level);
            GL.TexSubImage2D(TextureTarget.Texture2D,level,0,0,mipWidth,mipHeight,PixelFormat.Red,PixelType.Float,
                Enumerable.Repeat(depth,mipWidth*mipHeight).ToArray());
        }
    }
    /// <summary>Authors surviving foreground particles independently of the corrected material-depth input.</summary>
    internal void SetVisibilityForeground()
    {
        float[] depths=new float[width*height];
        for(int y=0;y<height;y++)for(int x=0;x<width;x++)depths[y*width+x]=Depth((x+y)%3==0?20:1000);
        visibility.UploadDataImmediate(depths);
    }
    /// <summary>Captures one explicit cloud/camera state, including per-pixel opaque and liquid discontinuities.</summary>
    internal void Capture(uint frame,float extinction,float environment,float scale,float offset,string scene)
    {
        shift=offset; detail=scene is "detail" or "detail-lit"; underwater=scene=="underwater";
        float aerialLoss=underwater ? .99f : scene=="aerial" ? .25f : 0;
        float aerialScatter=underwater?100:scene=="aerial"?2:0;
        aerial.UploadDataImmediate([aerialScatter/16,aerialScatter/16,aerialScatter/16,0,0,0,0,0],0,0,0,1,2,1,0);
        loss.UploadDataImmediate([aerialLoss,aerialLoss,aerialLoss,0],0,0,0,1,1,1,0);
        surfaceDepth=scene=="particle-before"?20:scene=="particle-within"?60:scene=="particle-behind"?160:0;
        var view=Mat4f.Create();
        if(scene=="rotation")
        {
            float angle=(frame%4)*.003f,c=MathF.Cos(angle),s=MathF.Sin(angle);
            view=[c,0,-s,0,0,1,0,0,s,0,c,0,0,0,0,1];
        }
        var inverseView=new float[16];Mat4f.Invert(inverseView,view);Mat4f.Mul(currentProjection,projection,view);
        if(frame==0)currentProjection.CopyTo(previousProjection,0);
        camera.Capture(projection,view,inverse,inverseView,previousProjection,currentProjection,new(width,height),0,frame,Vector3.Zero,Vector3.Zero,0,new(.1f,1000));
        storage.Capture(scale,frame);
        float[] columns=new float[map.Width*map.Width*4];for(int i=0;i<map.Width*map.Width;i++){columns[i*4]=scene=="cloud-edge"&&i%map.Width<map.Width/2?0:extinction;columns[i*4+2]=scene=="thin-height"?-.01f:-.5f;columns[i*4+3]=scene=="thin-height"?.01f:.5f;}
        map.UploadDataImmediate(columns);
        float[] depths=new float[width*height],liquids=new float[width*height];
        for(int y=0;y<height;y++)for(int x=0;x<width;x++)
        {
            bool foreground=(scene is "terrain" or "mixed")&&x<width/2 || scene=="moving-edge"&&(x+y)%3==0;
            depths[y*width+x]=Depth(foreground?20:1000);
            liquids[y*width+x]=Depth((scene is "liquid" or "mixed")&&y<height/2?60:1000);
        }
        opaque.UploadDataImmediate(depths);visibility.UploadDataImmediate(depths);liquid.UploadDataImmediate(liquids);
        var lighting=new AtmosphereLighting(Vector3.UnitY,scene is "lit" or "detail-lit"?new(30):Vector3.Zero,new(environment),Vector3.Zero,Vector3.Zero,ImmutableArray<float>.Empty)
            { Moon=-Vector3.UnitY,Lunar=scene is "lit" or "detail-lit"?new(3):Vector3.Zero,CalibrationGeneration=1,
                LookupStorageScale=LightingStorage.PersistentScale,ReconstructionMaximumNits=aerialScatter };
        var placement=new CloudPlacement(0,0,0,0,0,0,offset,0,new(offset,0,-50),0);
        capturedLighting = lighting; capturedPlacement = placement;
        Configure(reference,reference.Parameters,placement,lighting);Configure(trace,trace.Parameters,placement,lighting);
        resolve.SceneDepth=opaque.TextureId;resolve.VisibilityDepth=visibility.TextureId;resolve.LiquidDepth=liquid.TextureId;
    }
    #endregion
    #region Submission and history probes
    /// <summary>Authors independent directional sources while preserving the captured medium, environment and quality selection.</summary>
    internal void SetDirectionalLights(Vector3 sun, Vector3 moon, int sources)
    {
        var original = capturedLighting ?? throw new InvalidOperationException("Capture lighting before changing directions.");
        var lighting = original with { Sun = sun, Moon = moon,
            Solar = (sources & 1) != 0 ? original.Solar : Vector3.Zero,
            Lunar = (sources & 2) != 0 ? original.Lunar : Vector3.Zero };
        Configure(reference, reference.Parameters, capturedPlacement, lighting);
        Configure(trace, trace.Parameters, capturedPlacement, lighting);
    }
    /// <summary>Authors sparse stepped clouds and optional opaque/visibility/liquid foreground clips independently of the hierarchy.</summary>
    internal void AuthorHierarchyColumns(bool foreground)
    {
        float[] columns = new float[map.Width * map.Width * 4];
        for (int z = 0; z < map.Width; z++)
        for (int x = 0; x < map.Width; x++)
        {
            if ((x + 2 * z) % 4 == 0) continue;
            int index = (z * map.Width + x) * 4;
            columns[index] = .125f;
            columns[index + 2] = (x + z) % 3 == 0 ? 1 : -.5f;
            columns[index + 3] = columns[index + 2] + 1;
        }
        map.UploadDataImmediate(columns);
        if (!foreground) return;
        SetVisibilityForeground();
        opaque.UploadDataImmediate(Enumerable.Range(0, width * height).Select(i => Depth(i % 5 == 0 ? 20 : 1000)).ToArray());
        liquid.UploadDataImmediate(Enumerable.Range(0, width * height).Select(i => Depth(i % 7 == 0 ? 60 : 1000)).ToArray());
    }
    /// <summary>Binds only exact-source summaries; a deliberately mismatched certificate exercises explicit DDA fallback.</summary>
    internal void UseHierarchy(CloudHierarchy? hierarchy, bool mismatch = false)
    {
        if (hierarchy is null) { trace.SetHierarchy(null); return; }
        var input = new CloudMapInput(this, 1, 1, 1, 1, 1, 1,
            new(map.TextureId, 2, 3, 4, map.Width, 50, 5), default);
        Assert.True(hierarchy.TryBuild(input, () => true), hierarchy.Failure);
        bool available = hierarchy.TryGetCurrent(mismatch ? input with { Generation = 2 } : input, out var bounds);
        Assert.Equal(!mismatch, available);
        trace.SetHierarchy(available ? bounds : null);
    }
    /// <summary>Reads the complete traced OIT record including first/last cloud and foreground distances.</summary>
    internal float[][] ReadTraceRecord() => CloudPayloadReadback.Read(traced);
    /// <summary>Selects the allowed dense midpoint budget independently of reduced tracing.</summary>
    internal void SetReferenceSamples(int count) { referenceSamples=count; }
    /// <summary>Executes deterministic full-resolution midpoint transport without changing history.</summary>
    internal void RenderReference() { Draw(reference,expected.Target,true); }
    /// <summary>Executes one reduced sample phase without running temporal or spatial reconstruction.</summary>
    internal void RenderTrace(uint frame)
    {
        trace.Temporal.Capture(previousProjection,Vector3.Zero,lowWidth,lowHeight,frame,unchecked(frame-1),false);
        Draw(trace,traced.Target,false);
    }
    /// <summary>Authors separate sampling dimensions to isolate view and secondary transport behavior.</summary>
    internal void SetJitterDimensions(float view,float light)
    {
        var values=Own(DynamicTexture2D.Create(1,1,PixelInternalFormat.Rg32f));
        values.UploadDataImmediate(new[]{view,light});trace.PmjJitter=values;
    }
    /// <summary>Supplies a known physical reduced payload while the independent density map remains empty.</summary>
    internal float[][] ResolveAuthoredHistory(bool patterned=false)
    {
        for(int layer=0;layer<3;layer++)
        {
            int components=4;
            float[] values=new float[lowWidth*lowHeight*components];
            for(int pixel=0;pixel<lowWidth*lowHeight;pixel++)
            for(int channel=0;channel<components;channel++)
            {
                float factor=patterned?(float)(pixel%lowWidth+1)/lowWidth:1;
                values[pixel*components+channel]=layer==2?(channel==0?50:channel==1?100:Depth(1000)):
                    layer==0?(channel==3?1-factor*.6f:8*factor):channel<3?1-factor*.4f:0;
            }
            CloudPayloadReadback.Upload(historyA,layer,values);
        }
        resolve.Temporal.Capture(projection,Vector3.Zero,lowWidth,lowHeight,0,0,false);
        BindHistory(resolve,historyA); Draw(resolve,result.Target,true); return ReadDefault();
    }
    /// <summary>Executes the reduced trace, temporal filter and resolve inside one boundary before drawing the independent reference.</summary>
    internal void Render(uint frame,bool valid)
    {
        var previous=(frame&1)==0?historyA:historyB;var next=(frame&1)==0?historyB:historyA;
        foreach(var p in new[]{trace.Temporal,temporal.Temporal,resolve.Temporal})
            p.Capture(previousProjection,new(lastShift-shift,0,0),lowWidth,lowHeight,frame,unchecked(frame-1),valid);
        BindHistory(temporal,previous);BindHistory(resolve,next);
        temporal.CurrentPayload=traced.View;
        queuedPasses=[];
        Draw(trace,traced.Target,false);Draw(temporal,next.Target,false);Draw(resolve,result.Target,true);
        SubmitQueuedPasses();last=next;lastShift=shift;currentProjection.CopyTo(previousProjection,0);
        Draw(reference,expected.Target,true);
    }
    /// <summary>Authors distinguishable current/history payloads to prove interpolation and whole-record rejection on the GPU.</summary>
    internal float[][] ProbeHistory(string scenario)
    {
        for(int layer=0;layer<3;layer++)
        {
            int components=4;
            float[] a=new float[lowWidth*lowHeight*components],b=new float[a.Length];
            for(int pixel=0;pixel<lowWidth*lowHeight;pixel++)
            {
                float current=pixel==4?.4f:pixel%2==0?.2f:.8f;
                for(int channel=0;channel<components;channel++)
                {
                    int index=pixel*components+channel;
                    a[index]=current*((layer==0&&channel<3)?10:1);b[index]=.42f*((layer==0&&channel<3)?10:1);
                    if(scenario=="weather"&&layer==0&&channel==3)b[index]=.8f;
                    if(scenario=="buckets"&&layer==1&&channel==0)b[index]=.8f;
                    if(layer==2)
                    {
                        a[index]=b[index]=channel==0?10:channel==1?20:1;
                        if(scenario=="depth"&&channel==2)b[index]=.9f;
                    }
                }
            }
            if(scenario=="nan"&&layer==0)b[4*4]=float.NaN;
            CloudPayloadReadback.Upload(traced,layer,a);CloudPayloadReadback.Upload(historyA,layer,b);
        }
        temporal.Temporal.Capture(projection,scenario=="motion"?new(100,0,0):Vector3.Zero,lowWidth,lowHeight,0,0,scenario!="invalid");
        temporal.CurrentPayload=traced.View;
        BindHistory(temporal,historyA);Draw(temporal,historyB.Target,false);last=historyB;return ReadHistory();
    }
    /// <summary>Advances a fixed trace through alternate history storage to expose the bounded temporal recurrence.</summary>
    internal float[][] AdvanceProbeHistory()
    {
        var next=ReferenceEquals(last,historyA)?historyB:historyA;
        BindHistory(temporal,last!);Draw(temporal,next.Target,false);last=next;return ReadHistory();
    }
    #endregion
    #region Readback and disposal
    /// <summary>Reads test-only full-resolution reconstructed payloads.</summary>
    internal float[][] ReadDefault()=>result.Images.Take(6).Select(image=>image.ReadPixels()).ToArray();
    /// <summary>Reads test-only direct reference payloads for numerical comparison.</summary>
    internal float[][] ReadReference()=>expected.Images.Take(6).Select(image=>image.ReadPixels()).ToArray();
    /// <summary>Reads reduced transport independently of temporal accumulation.</summary>
    internal float[][] ReadTrace()=>ReadNativePayload(traced);
    /// <summary>Reads physical history to verify exposure-independent storage.</summary>
    internal float[][] ReadHistory()=>ReadNativePayload(last!);
    /// <summary>Releases fixture-owned resources in reverse dependency order.</summary>
    public void Dispose(){for(int i=owned.Count-1;i>=0;i--)owned[i].Dispose();}
    #endregion
    #endregion
    #region Private
    /// <summary>Allocates an independent array for each block while retaining identical local layer addressing.</summary>
    private CloudVolumeSurface CreateIndependentBlock(string name)
    {
        var array=Own(DynamicTexture3D.Create(lowWidth,lowHeight,3,PixelInternalFormat.Rgba32f,textureTarget:TextureTarget.Texture2DArray,immutableStorage:true));array.SetDebugName(name);
        return Own(new CloudVolumeSurface(array,0,name));
    }
    /// <summary>Converts compact physical storage into native OIT channel order for reference comparisons.</summary>
    private static float[][] ReadNativePayload(CloudVolumeSurface surface)
    {
        var images=CloudPayloadReadback.Read(surface);
        int pixels=images[0].Length/4;
        float[] bins=new float[pixels*3],transmission=new float[pixels],glow=new float[pixels*4];
        float[][] accumulation=Enumerable.Range(0,3).Select(_=>new float[pixels*4]).ToArray();
        for(int pixel=0;pixel<pixels;pixel++)
        {
            float t=images[0][pixel*4+3];transmission[pixel]=t;glow[pixel*4+3]=1-t;
            for(int bucket=0;bucket<3;bucket++)
            {
                float b=images[1][pixel*4+bucket];bins[pixel*3+bucket]=b;
                for(int channel=0;channel<4;channel++)
                    accumulation[bucket][pixel*4+channel]=(channel==3?1-t:images[0][pixel*4+channel])*(1-b);
            }
        }
        return [bins,transmission,glow,..accumulation];
    }
    /// <summary>Records an owned fixture allocation without transferring any production resource.</summary>
    private T Own<T>(T resource) where T:IDisposable {owned.Add(resource);return resource;}
    /// <summary>Supplies identical physical transport inputs to all tracing consumers.</summary>
    private void Configure(ICloudVolumeBindings program,CloudVolumeParameters parameters,CloudPlacement placement,AtmosphereLighting lighting)
    {
        parameters.CaptureMap(map.Width,placement,1,CloudVolumeSettings.Default with {Detail=detail,ViewSamples=ReferenceEquals(program,reference)?referenceSamples:4});
        parameters.CaptureLighting(lighting,underwater?1:0,new(underwater ? .02f : 0,0,0,0),Vector3.Zero);
        program.Map=map.TextureId;program.SceneDepth=opaque.TextureId;program.VisibilityDepth=visibility.TextureId;program.LiquidDepth=liquid.TextureId;program.AerialRadiance=aerial;program.AerialAttenuation=loss;
    }
    /// <summary>Binds an alternate physical-history set, including unfiltered depth metadata.</summary>
    private static void BindHistory(ICloudHistoryBindings program,CloudVolumeSurface surface)
    {
        program.HistoryPayload=surface.View;
    }
    /// <summary>Uses replacement writes internally and the real six-target native blend policy for final composition.</summary>
    private void Draw(GpuProgram program,GpuFramebuffer target,bool oit)
    {
        RenderPassColor[] outputs=oit?[new(0,AttachmentLoad.Clear,Clear:ColorClearValue.Float(1,1,1,1)),new(1,AttachmentLoad.Clear,Clear:ColorClearValue.Float(1,1,1,1)),
            new(2,AttachmentLoad.Clear,Clear:ColorClearValue.Float(0,0,0,0)),new(3,AttachmentLoad.Clear,Clear:ColorClearValue.Float(0,0,0,0)),
            new(4,AttachmentLoad.Clear,Clear:ColorClearValue.Float(0,0,0,0)),new(5,AttachmentLoad.Clear,Clear:ColorClearValue.Float(0,0,0,0))]:CloudVolumeSurface.Outputs;
        if(oit&&surfaceDepth>0)
        {
            // Independently seed an ordinary translucent surface's native OIT payload at
            // before/within/behind depths; the cloud must preserve existing bucket composition.
            float bin=MathF.Log(surfaceDepth/30+1),alpha=.25f;
            float[] weights=Enumerable.Range(0,3).Select(i=>i==2&&bin>2?1:MathF.Exp(-MathF.Pow((bin-i)/.832f,2))).ToArray();
            var colors=new List<RenderPassColor>{new(0,AttachmentLoad.Clear,Clear:ColorClearValue.Float(1-alpha*weights[0],1-alpha*weights[1],1-alpha*weights[2],1)),
                new(1,AttachmentLoad.Clear,Clear:ColorClearValue.Float(1-alpha,0,0,0)),new(2,AttachmentLoad.Clear,Clear:ColorClearValue.Float(0,0,0,.2f))};
            for(int i=0;i<3;i++)
            {
                float a=alpha*MathF.Exp(-surfaceDepth/3000)*weights[i];
                colors.Add(new(i+3,AttachmentLoad.Clear,Clear:ColorClearValue.Float(3*a,2*a,a,a)));
            }
            outputs=colors.ToArray();
        }
        var key=(program,target.FboId,oit,surfaceDepth);
        if(!prepared.TryGetValue(key,out var entry))
        {
            var description=new RenderPassDesc(target,outputs);using var metadata=new RenderPassTargets(description);
            var pipeline=Own(new GraphicsPipeline(lifetime,new(program.GraphicsIdentity!,CloudVolumeRenderer.Layout,metadata.Signature,DynamicPipelineState.Viewport,
                blending:oit?LiquidPipelineStates.SurfaceBlending:null),program));
            prepared.Add(key,entry=(description,pipeline));
        }
        var pass=entry.Pass;
        if(queuedPasses is not null){queuedPasses.Add(entry);return;}
        int vao=GL.GetInteger(GetPName.VertexArrayBinding),fbo=GL.GetInteger(GetPName.DrawFramebufferBinding);
        Assert.True(GraphicsCommandContext.TryRun("Tests.Cloud.Reconstruction",[entry.Pipeline],true,commands=>{
            commands.BeginPass(pass);commands.SetPipeline(entry.Pipeline);commands.SetDynamicState(new(){Viewport=commands.PassViewport});commands.Draw(geometry,new(0,3));commands.EndPass();}));
        Assert.Equal(vao,GL.GetInteger(GetPName.VertexArrayBinding));Assert.Equal(fbo,GL.GetInteger(GetPName.DrawFramebufferBinding));
    }
    /// <summary>Submits all three prepared passes inside one engine interruption, matching production scope ownership.</summary>
    private void SubmitQueuedPasses()
    {
        var passes=queuedPasses??throw new InvalidOperationException("No queued cloud passes.");queuedPasses=null;
        int vao=GL.GetInteger(GetPName.VertexArrayBinding),fbo=GL.GetInteger(GetPName.DrawFramebufferBinding);
        Assert.True(GraphicsCommandContext.TryRun("Tests.Cloud.Reconstruction",passes.Select(pass=>pass.Pipeline).ToArray(),true,commands=>{
            foreach(var entry in passes)
            {
                commands.BeginPass(entry.Pass);commands.SetPipeline(entry.Pipeline);
                commands.SetDynamicState(new(){Viewport=commands.PassViewport});commands.Draw(geometry,new(0,3));commands.EndPass();
            }
        }));
        Assert.Equal(vao,GL.GetInteger(GetPName.VertexArrayBinding));Assert.Equal(fbo,GL.GetInteger(GetPName.DrawFramebufferBinding));
    }
    /// <summary>Independently authors perspective depth for the controlled projection.</summary>
    private static float Depth(float distance)=>(1.00020002f-.200020002f/distance)*.5f+.5f;
    #endregion
}
