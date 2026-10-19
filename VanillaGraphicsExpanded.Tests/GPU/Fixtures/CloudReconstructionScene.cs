using System.Collections.Immutable;
using System.Numerics;
using System.Runtime.InteropServices;
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
using VanillaGraphicsExpanded.Rendering.Shaders.Fixtures;
using Vintagestory.API.MathTools;

namespace VanillaGraphicsExpanded.Tests.GPU.Fixtures;

/// <summary>Owns synthetic native inputs and executes the same three pass structure as the runtime.</summary>
internal sealed class CloudReconstructionScene : IDisposable
{
    private readonly int width,height,lowWidth,lowHeight,traceWidth,traceHeight;
    private readonly CloudVolumeSettings settings;
    private readonly ComponentShaderPrograms programs;
    private CloudVolumeProgram? reference;
    private CloudTraceProgram? trace;
    private CloudTemporalProgram? temporal;
    private CloudResolveProgram? resolve;
    private readonly List<IDisposable> owned=[];
    private readonly VgeFrameUniformBuffer camera;
    private readonly TestFrameLightingPublication storage;
    private readonly DynamicTexture2D map,opaque,visibility,liquid;
    private readonly DynamicTexture3D aerial,loss;
    private readonly CloudVolumeSurface traced,historyA,historyB;
    private readonly CloudOitSurface result;
    private CloudOitSurface? expected;
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
    private float? localFogDensity;
    private AtmosphereLighting? capturedLighting,configuredLighting;
    private CloudPlacement capturedPlacement;
    private CloudTraceProgram? shadingProbe;
    private DynamicTexture2D? shadowCache;
    private long mapPublication,cachePublication;
    private int cacheSource;
    private uint capturedFrame;
    private AtmosphereLighting? cacheLighting;
    #region Public API
    #region View preparation
    /// <summary>Allocates small deterministic targets and independently authored column/depth/atmospheric fixtures.</summary>
    internal CloudReconstructionScene(int width,int height,ComponentShaderPrograms programs,bool mipmappedOpaque,int nativeMapWidth,bool independentStorage=false,CloudVolumeSettings? settings=null)
    {
        this.width=width;this.height=height;this.programs=programs;
        this.settings=settings??CloudVolumeSettings.Default;
        this.settings.Validate();
        var layout=CloudResolutionLayout.Create(width,height,this.settings.TraceScale);
        (lowWidth,lowHeight)=(layout.Width,layout.Height);
        (traceWidth,traceHeight)=(layout.TraceWidth,layout.TraceHeight);
        camera=Own(new VgeFrameUniformBuffer()); storage=Own(new TestFrameLightingPublication());
        map=Own(DynamicTexture2D.Create(nativeMapWidth,nativeMapWidth,PixelInternalFormat.Rgba32f));
        int levels=1;for(int extent=Math.Max(width,height);extent>1;extent>>=1)levels++;
        opaque=Own(mipmappedOpaque?DynamicTexture2D.CreateMipmapped(width,height,PixelInternalFormat.R32f,levels):
            DynamicTexture2D.Create(width,height,PixelInternalFormat.R32f));
        visibility=Own(DynamicTexture2D.Create(width,height,PixelInternalFormat.R32f));
        liquid=Own(DynamicTexture2D.Create(width,height,PixelInternalFormat.R32f));
        aerial=Own(DynamicTexture3D.Create(1,2,2,PixelInternalFormat.Rgba32f,textureTarget:TextureTarget.Texture3D));
        loss=Own(DynamicTexture3D.Create(1,1,2,PixelInternalFormat.Rgba32f,textureTarget:TextureTarget.Texture3D));
        aerial.UploadDataImmediate(new float[16],0,0,0,1,2,2,0);loss.UploadDataImmediate(new float[8],0,0,0,1,1,2,0);
        if(independentStorage)
        {
            // Only storage sharing differs; both fixtures execute the current shader algorithm.
            traced=CreateIndependentBlock("Tests.Cloud.IndependentTrace",traceWidth,traceHeight);
            historyA=CreateIndependentBlock("Tests.Cloud.IndependentA",lowWidth,lowHeight);historyB=CreateIndependentBlock("Tests.Cloud.IndependentB",lowWidth,lowHeight);
        }
        else if(this.settings.TraceScale==CloudTraceScale.Quarter)
        {
            traced=Own(new CloudVolumeStorage(traceWidth,traceHeight,"Tests.Cloud.Trace",1)).Blocks[0];
            var histories=Own(new CloudVolumeStorage(lowWidth,lowHeight,"Tests.Cloud.History",2));
            historyA=histories.Blocks[0];historyB=histories.Blocks[1];
        }
        else
        {
            var payloads=Own(new CloudVolumeStorage(lowWidth,lowHeight,"Tests.Cloud.Payloads"));
            traced=payloads.Blocks[0];historyA=payloads.Blocks[1];historyB=payloads.Blocks[2];
        }
        result=Own(new CloudOitSurface(width,height,"Tests.Cloud.Default"));
        lifetime=Own(new GraphicsPipelineLifetime());
        geometry=Own(new ArrayGraphicsGeometry(CloudVolumeRenderer.Layout,PrimitiveType.Triangles,new Dictionary<int,GpuVbo>(),proceduralVertices:3));
        Mat4f.Invert(inverse,projection);
    }
    /// <summary>Seeds adversarial coarse levels and enables mip selection so an accidental implicit LOD becomes observable.</summary>
    internal void SetOpaqueCoarseDepth(float depth)
    {
        opaque.SetTexFilter(TextureMinFilter.NearestMipmapNearest,TextureMagFilter.Nearest);
        for(int level=1;level<opaque.MipLevels;level++)
        {
            using var image=GpuFramebufferAttachment.FromTexture(opaque,level);
            using var target=StaticGpuFramebuffer.Create([image]);
            var pass=new RenderPassDesc(target,[new(0,AttachmentLoad.Clear,Clear:ColorClearValue.Float(depth,0,0,0))]);
            Assert.True(GraphicsCommandContext.TryRun("Tests.Cloud.CoarseDepth",[],true,commands=>
            {commands.BeginPass(pass);commands.EndPass();}));
        }
    }
    /// <summary>Authors surviving foreground particles independently of the corrected material-depth input.</summary>
    internal void SetVisibilityForeground()
    {
        float[] depths=new float[width*height];
        for(int y=0;y<height;y++)for(int x=0;x<width;x++)depths[y*width+x]=Depth((x+y)%3==0?20:1000);
        visibility.UploadDataImmediate(depths);
    }
    /// <summary>Authors uniform independent clip planes without the patterned scene's half-extent classification.</summary>
    internal void SetUniformDepthClips(float opaqueDistance,float visibilityDistance,float liquidDistance)
    {
        // Explicit scalar planes also cover a one-pixel view, where a half-extent stencil is empty.
        opaque.UploadDataImmediate(Enumerable.Repeat(Depth(opaqueDistance),width*height).ToArray());
        visibility.UploadDataImmediate(Enumerable.Repeat(Depth(visibilityDistance),width*height).ToArray());
        liquid.UploadDataImmediate(Enumerable.Repeat(Depth(liquidDistance),width*height).ToArray());
    }
    /// <summary>Authors full-view depth identities for independently checking scheduled sample locations.</summary>
    internal void SetDepthFields(float[] opaqueValues,float[] visibilityValues,float[] liquidValues)
    {
        Assert.Equal(width*height,opaqueValues.Length);Assert.Equal(width*height,visibilityValues.Length);Assert.Equal(width*height,liquidValues.Length);
        opaque.UploadDataImmediate(opaqueValues);visibility.UploadDataImmediate(visibilityValues);liquid.UploadDataImmediate(liquidValues);
    }
    /// <summary>Captures one explicit cloud/camera state, including per-pixel opaque and liquid discontinuities.</summary>
    internal void Capture(uint frame,float extinction,float environment,float scale,float offset,string scene)
    {
        capturedFrame=frame;mapPublication++;
        shift=offset; detail=scene is "detail" or "detail-lit"; underwater=scene=="underwater";localFogDensity=null;
        float aerialLoss=underwater ? .99f : scene=="aerial" ? .25f : 0;
        float aerialScatter=underwater?100:scene=="aerial"?2:0;
        aerial.UploadDataImmediate([aerialScatter/16,aerialScatter/16,aerialScatter/16,0,0,0,0,0,aerialScatter/16,aerialScatter/16,aerialScatter/16,0,0,0,0,0],0,0,0,1,2,2,0);
        loss.UploadDataImmediate([aerialLoss,aerialLoss,aerialLoss,0,aerialLoss,aerialLoss,aerialLoss,0],0,0,0,1,1,2,0);
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
        float[] columns=new float[map.Width*map.Width*4];for(int i=0;i<map.Width*map.Width;i++){columns[i*4]=scene=="cloud-edge"&&i%map.Width<map.Width/2?0:extinction;columns[i*4+2]=scene=="homogeneous"?-10:scene=="thin-height"?-.01f:-.5f;columns[i*4+3]=scene=="homogeneous"?10:scene=="thin-height"?.01f:.5f;}
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
        capturedLighting = configuredLighting = lighting; capturedPlacement = placement;
        if(reference is not null)Configure(reference,reference.Parameters,placement,lighting);
        if(trace is not null)Configure(trace,trace.Parameters,placement,lighting);
    }
    #endregion
    #region Submission and history probes
    /// <summary>Authors a distance-varying atmospheric lookup for explicit transport diagnostics.</summary>
    internal void SetAerialDistanceRamp(float scatter,float attenuation)
    {
        var lighting=(configuredLighting??throw new InvalidOperationException("Capture lighting before changing aerial transport."))
            with {ReconstructionMaximumNits=scatter};
        capturedLighting=configuredLighting=lighting;
        aerial.SetTexFilter(TextureMinFilter.Linear,TextureMagFilter.Linear);
        loss.SetTexFilter(TextureMinFilter.Linear,TextureMagFilter.Linear);
        aerial.UploadDataImmediate([0,0,0,0,0,0,0,0,scatter/16,scatter/16,scatter/16,0,0,0,0,0],0,0,0,1,2,2,0);
        loss.UploadDataImmediate([0,0,0,0,attenuation,attenuation,attenuation,0],0,0,0,1,1,2,0);
        if(reference is not null)Configure(reference,reference.Parameters,capturedPlacement,lighting);
        if(trace is not null)Configure(trace,trace.Parameters,capturedPlacement,lighting);
    }
    /// <summary>Changes only the active local-medium density while preserving the captured lighting and placement.</summary>
    internal void SetLocalFogDensity(float density)
    {
        localFogDensity=density;
        var lighting=configuredLighting??throw new InvalidOperationException("Capture lighting before changing fog.");
        if(reference is not null)Configure(reference,reference.Parameters,capturedPlacement,lighting);
        if(trace is not null)Configure(trace,trace.Parameters,capturedPlacement,lighting);
    }
    /// <summary>Authors independent directional sources while preserving the captured medium, environment and quality selection.</summary>
    internal AtmosphereLighting SetDirectionalLights(Vector3 sun, Vector3 moon, int sources, float? solarIlluminance = null, float? lunarIlluminance = null)
    {
        var original = capturedLighting ?? throw new InvalidOperationException("Capture lighting before changing directions.");
        var lighting = original with { Sun = sun, Moon = moon,
            Solar = (sources & 1) != 0 ? solarIlluminance.HasValue ? new Vector3(solarIlluminance.Value) : original.Solar : Vector3.Zero,
            Lunar = (sources & 2) != 0 ? lunarIlluminance.HasValue ? new Vector3(lunarIlluminance.Value) : original.Lunar : Vector3.Zero };
        configuredLighting=lighting;
        if(reference is not null)Configure(reference,reference.Parameters,capturedPlacement,lighting);
        if(trace is not null)Configure(trace,trace.Parameters,capturedPlacement,lighting);
        return lighting;
    }
    /// <summary>Authors sparse stepped clouds and optional opaque/visibility/liquid foreground clips independently of the hierarchy.</summary>
    internal void AuthorHierarchyColumns(bool foreground)
    {
        mapPublication++;
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
    /// <summary>Binds only exact-source summaries; a deliberately mismatched certificate exercises explicit direct support fallback.</summary>
    internal void UseHierarchy(CloudHierarchy? hierarchy, bool mismatch = false)
    {
        if (hierarchy is null) { Trace.SetHierarchy(null); return; }
        var input = new CloudMapInput(this, 1, 1, mapPublication, 1, mapPublication, capturedFrame,
            new(map.TextureId, 2, 3, 4, map.Width, 50, 5), capturedPlacement);
        Assert.True(hierarchy.TryBuild(input, () => true), hierarchy.Failure);
        bool available = hierarchy.TryGetCurrent(mismatch ? input with { Generation = input.Generation+1 } : input, out var bounds);
        Assert.Equal(!mismatch, available);
        Trace.SetHierarchy(available ? bounds : null);
    }
    /// <summary>Generates one same-frame diagnostic attenuation image using certified root bounds and shared physical inputs.</summary>
    internal void BuildShadowCache(CloudShadowAttenuationComputeShader builder,CloudHierarchy hierarchy,
        DynamicTexture2D target,int quadrature,int source,GpuTimestampQuery? rootComplete=null)
    {
        if(shadingProbe is null)
        {
            shadingProbe=programs.Create<CloudTraceProgram>();shadingProbe.Frame=camera;shadingProbe.Lighting=storage.Buffer;
            shadingProbe.PrimaryNoise=Own(CloudBlueNoiseTexture.Create());

        }
        Configure(shadingProbe,shadingProbe.Parameters,capturedPlacement,configuredLighting!);
        var input=new CloudMapInput(this,1,1,mapPublication,1,mapPublication,capturedFrame,
            new(map.TextureId,2,3,4,map.Width,50,5),capturedPlacement);
        Assert.True(hierarchy.TryBuild(input,()=>true),hierarchy.Failure);
        Assert.True(hierarchy.TryGetCurrent(input,out var bounds));
        rootComplete?.Issue();
        int segments=(target.Height/target.Width-1)*4;
        shadingProbe.SetHierarchy(bounds);builder.Capture(shadingProbe.Parameters,bounds!,target.Width,quadrature,source,segments);
        builder.Map=map;builder.Target=new(target,TextureAccess.WriteOnly);
        builder.Dispatch((target.Width+7)/8,(target.Width+7)/8);
        GpuComputePipeline.MemoryBarrier(MemoryBarrierFlags.ShaderImageAccessBarrierBit|MemoryBarrierFlags.TextureFetchBarrierBit);
        shadowCache=target;cachePublication=mapPublication;cacheLighting=configuredLighting;cacheSource=source;
    }
    /// <summary>Authors native column controls for finite shadow-generation arithmetic checks.</summary>
    internal void SetShadowColumns(float[] columns)
    {
        if(columns.Length!=map.Width*map.Height*4) throw new ArgumentException("Native map shape does not match columns.",nameof(columns));
        map.UploadDataImmediate(columns);
        mapPublication++;
    }
    /// <summary>Runs all three fixture passes with cached lighting, falling back to marching after source or map changes.</summary>
    internal void RenderShadowCache(uint frame,bool valid)
    {
        var probe=shadingProbe??throw new InvalidOperationException("Generate the diagnostic cache before drawing.");
        Configure(probe,probe.Parameters,capturedPlacement,configuredLighting!);
        if(cachePublication!=mapPublication)probe.SetHierarchy(null);
        probe.SetShadowCache(cachePublication==mapPublication&&ReferenceEquals(cacheLighting,configuredLighting)?shadowCache:null,cacheSource);
        var previous=last??historyA;var next=ReferenceEquals(previous,historyA)?historyB:historyA;
        foreach(var p in new[]{probe.Temporal,Temporal.Temporal,Resolve.Temporal})
            p.Capture(previousProjection,new(lastShift-shift,0,0),lowWidth,lowHeight,frame,unchecked(frame-1),valid,settings.TraceScale);
        BindHistory(Temporal,previous);BindHistory(Resolve,next);Temporal.CurrentPayload=traced.View;
        queuedPasses=[];Draw(probe,traced.Target,false);Draw(Temporal,next.Target,false);Draw(Resolve,result.Target,true);
        SubmitQueuedPasses();last=next;lastShift=shift;currentProjection.CopyTo(previousProjection,0);
    }
    /// <summary>Reads the complete traced OIT record including first/last cloud and foreground distances.</summary>
    internal float[][] ReadTraceRecord() => CloudPayloadReadback.Read(traced);
    /// <summary>Copies the actual staged volume and sampling constants for opt-in measurement receipts.</summary>
    internal float[][] ReadTraceParameters() => [MemoryMarshal.Cast<byte,float>(Trace.Parameters.Bytes).ToArray(),
        MemoryMarshal.Cast<byte,float>(Trace.Temporal.Bytes).ToArray()];
    /// <summary>Copies the diagnostic program's actual staged physical and sampling blocks before invalidation probes.</summary>
    internal float[][] ReadShadowCacheParameters()
    {
        var probe=shadingProbe??throw new InvalidOperationException("Generate the diagnostic cache before reading its inputs.");
        return [MemoryMarshal.Cast<byte,float>(probe.Parameters.Bytes).ToArray(),MemoryMarshal.Cast<byte,float>(probe.Temporal.Bytes).ToArray()];
    }
    /// <summary>Executes one reduced sample phase without running temporal or spatial reconstruction.</summary>
    internal void RenderTrace(uint frame)
    {
        Trace.Temporal.Capture(previousProjection,Vector3.Zero,lowWidth,lowHeight,frame,unchecked(frame-1),false,settings.TraceScale);
        Draw(Trace,traced.Target,false);
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
        Resolve.Temporal.Capture(projection,Vector3.Zero,lowWidth,lowHeight,0,0,false,settings.TraceScale);
        BindHistory(Resolve,historyA); Draw(Resolve,result.Target,true); return ReadDefault();
    }
    /// <summary>Executes reduced transport and reconstruction, drawing the independent reference only when explicitly requested.</summary>
    internal void Render(uint frame,bool valid,bool includeReference=false)
    {
        var previous=(frame&1)==0?historyA:historyB;var next=(frame&1)==0?historyB:historyA;
        foreach(var p in new[]{Trace.Temporal,Temporal.Temporal,Resolve.Temporal})
            p.Capture(previousProjection,new(lastShift-shift,0,0),lowWidth,lowHeight,frame,unchecked(frame-1),valid,settings.TraceScale);
        BindHistory(Temporal,previous);BindHistory(Resolve,next);
        Temporal.CurrentPayload=traced.View;
        queuedPasses=[];
        Draw(Trace,traced.Target,false);Draw(Temporal,next.Target,false);Draw(Resolve,result.Target,true);
        SubmitQueuedPasses();last=next;lastShift=shift;currentProjection.CopyTo(previousProjection,0);
        if(includeReference)
        {
            RenderReference();
        }
    }
    /// <summary>Executes history-free full-view transport without tracing or reconstructing reduced targets.</summary>
    internal void RenderReference()
    {
        expected??=Own(new CloudOitSurface(width,height,"Tests.Cloud.Reference"));
        Draw(Reference,expected.Target,true);
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
        Temporal.Temporal.Capture(projection,scenario=="motion"?new(100,0,0):Vector3.Zero,lowWidth,lowHeight,0,0,scenario!="invalid",settings.TraceScale);
        Temporal.CurrentPayload=traced.View;
        BindHistory(Temporal,historyA);Draw(Temporal,historyB.Target,false);last=historyB;return ReadHistory();
    }
    /// <summary>Feeds independent physical factors through the actual temporal program without a density or lighting march.</summary>
    internal float[][] ReconstructAuthoredTrace(uint frame,bool valid,float[][] current,float[][]? previous=null)
    {
        for(int layer=0;layer<3;layer++)
        {
            Assert.Equal(traceWidth*traceHeight*4,current[layer].Length);
            CloudPayloadReadback.Upload(traced,layer,current[layer]);
            if(previous is not null)CloudPayloadReadback.Upload(historyA,layer,previous[layer]);
        }
        Temporal.Temporal.Capture(projection,Vector3.Zero,lowWidth,lowHeight,frame,unchecked(frame-1),valid,settings.TraceScale);
        Temporal.CurrentPayload=traced.View;BindHistory(Temporal,historyA);
        Draw(Temporal,historyB.Target,false);last=historyB;
        return CloudPayloadReadback.Read(historyB);
    }
    /// <summary>Advances a fixed trace through alternate history storage to expose the bounded temporal recurrence.</summary>
    internal float[][] AdvanceProbeHistory()
    {
        var next=ReferenceEquals(last,historyA)?historyB:historyA;
        BindHistory(Temporal,last!);Draw(Temporal,next.Target,false);last=next;return ReadHistory();
    }
    #endregion
    #region Readback and disposal
    /// <summary>Reads test-only full-resolution reconstructed payloads.</summary>
    internal float[][] ReadDefault()=>result.Images.Take(6).Select(image=>image.ReadPixels()).ToArray();
    /// <summary>Reads test-only direct reference payloads for numerical comparison.</summary>
    internal float[][] ReadReference()=>(expected??throw new InvalidOperationException("Render the reference explicitly before reading it.")).Images.Take(6).Select(image=>image.ReadPixels()).ToArray();
    /// <summary>Reads reduced transport independently of temporal accumulation.</summary>
    internal float[][] ReadTrace()=>ReadNativePayload(traced);
    /// <summary>Reads all compact history factors and per-location metadata without reconstructing native products.</summary>
    internal float[][] ReadHistoryRecord()=>CloudPayloadReadback.Read(last!);
    /// <summary>Reads physical history to verify exposure-independent storage.</summary>
    internal float[][] ReadHistory()=>ReadNativePayload(last!);
    /// <summary>Releases fixture-owned resources in reverse dependency order.</summary>
    public void Dispose(){for(int i=owned.Count-1;i>=0;i--)owned[i].Dispose();}
    #endregion
    #endregion
    #region Private
    #region Demand-loaded programs
    /// <summary>Loads direct transport only when the caller requests a reference draw.</summary>
    private CloudVolumeProgram Reference
    {
        get
        {
            if(reference is not null)return reference;
            reference=programs.Create<CloudVolumeProgram>(p=>p.SelfShadowing=settings.ShadowQuality>0);reference.Frame=camera;reference.Lighting=storage.Buffer;
            if(configuredLighting is not null)Configure(reference,reference.Parameters,capturedPlacement,configuredLighting);
            return reference;
        }
    }
    /// <summary>Loads primary transport only when tracing is exercised.</summary>
    private CloudTraceProgram Trace
    {
        get
        {
            if(trace is not null)return trace;
            trace=programs.Create<CloudTraceProgram>(p=>p.SelfShadowing=settings.ShadowQuality>0);trace.Frame=camera;trace.Lighting=storage.Buffer;
            trace.PrimaryNoise=Own(CloudBlueNoiseTexture.Create());
            if(configuredLighting is not null)Configure(trace,trace.Parameters,capturedPlacement,configuredLighting);
            return trace;
        }
    }
    /// <summary>Loads temporal reconstruction with the current depth owners independently of transport.</summary>
    private CloudTemporalProgram Temporal
    {
        get
        {
            if(temporal is not null)return temporal;
            temporal=programs.Create<CloudTemporalProgram>();temporal.Frame=camera;
            temporal.SceneDepth=opaque.TextureId;temporal.VisibilityDepth=visibility.TextureId;temporal.LiquidDepth=liquid.TextureId;
            return temporal;
        }
    }
    /// <summary>Loads the texture-only OIT resolve independently of tracing and temporal reconstruction.</summary>
    private CloudResolveProgram Resolve
    {
        get
        {
            if(resolve is not null)return resolve;
            resolve=programs.Create<CloudResolveProgram>();resolve.Frame=camera;resolve.Lighting=storage.Buffer;
            resolve.SceneDepth=opaque.TextureId;resolve.VisibilityDepth=visibility.TextureId;resolve.LiquidDepth=liquid.TextureId;
            return resolve;
        }
    }
    #endregion
    #region Scene execution

    /// <summary>Allocates an independent array for each block while retaining identical local layer addressing.</summary>
    private CloudVolumeSurface CreateIndependentBlock(string name,int blockWidth,int blockHeight)
    {
        var array=Own(DynamicTexture3D.Create(blockWidth,blockHeight,3,PixelInternalFormat.Rgba32f,textureTarget:TextureTarget.Texture2DArray,immutableStorage:true));array.SetDebugName(name);
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
        parameters.CaptureMap(map.Width,placement,1,settings with
        {
            Detail=detail
        });
        parameters.CaptureLighting(lighting,underwater?1:0,new(localFogDensity??(underwater ? .02f : 0),0,0,0),Vector3.Zero);
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
        Assert.True(GraphicsCommandContext.TryRun("Tests.Cloud.Reconstruction",[entry.Pipeline],true,commands=>{
            commands.BeginPass(pass);commands.SetPipeline(entry.Pipeline);commands.SetDynamicState(new(){Viewport=commands.PassViewport});commands.Draw(geometry,new(0,3));commands.EndPass();}));
    }
    /// <summary>Submits all three prepared passes inside one engine interruption, matching production scope ownership.</summary>
    private void SubmitQueuedPasses()
    {
        var passes=queuedPasses??throw new InvalidOperationException("No queued cloud passes.");queuedPasses=null;
        Assert.True(GraphicsCommandContext.TryRun("Tests.Cloud.Reconstruction",passes.Select(pass=>pass.Pipeline).ToArray(),true,commands=>{
            foreach(var entry in passes)
            {
                commands.BeginPass(entry.Pass);commands.SetPipeline(entry.Pipeline);
                commands.SetDynamicState(new(){Viewport=commands.PassViewport});commands.Draw(geometry,new(0,3));commands.EndPass();
            }
        }));
    }
    /// <summary>Independently authors perspective depth for the controlled projection.</summary>
    private static float Depth(float distance)=>(1.00020002f-.200020002f/distance)*.5f+.5f;
    #endregion
    #endregion
}
