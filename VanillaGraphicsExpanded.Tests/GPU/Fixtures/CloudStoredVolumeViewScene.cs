using System;
using VanillaGraphicsExpanded.Rendering.RHI;
using System.Collections.Immutable;
using System.Numerics;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.PBR.Atmosphere;
using VanillaGraphicsExpanded.PBR.Clouds;
using VanillaGraphicsExpanded.PBR.Clouds.Volume;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Rendering.Integration;
using VanillaGraphicsExpanded.Rendering.Shaders;
using VanillaGraphicsExpanded.Rendering.Pipeline;
using VanillaGraphicsExpanded.Rendering.Pipeline.Passes;
using VanillaGraphicsExpanded.Rendering.Pipeline.Descriptions;
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
    private readonly CloudVolumeTemporalProgram temporalProgram;
    private readonly CloudOitSurface output;
    private readonly DynamicGpuFramebuffer engineOutput;
    private CloudMapInput input;
    private uint updateFrame;
    private object? replacementIdentity;
    private CloudVolumeViewScene preparedScene=null!;
    /// <summary>Reports only the view owner's allocated trace products and reconstruction metadata.</summary>
    internal long ViewTargetBytes=>renderer.TargetBytes;
#if DEBUG
    /// <summary>Counts immutable pass preparation independently of immediate draw validation.</summary>
    internal int ViewPassPreparations=>renderer.PassPreparations;
#endif
    /// <summary>Exposes retained density identity so view-only changes cannot silently replace the cloud fields.</summary>
    internal DynamicTexture3D StoredDensity=>storage.Published.Density;
    /// <summary>Reads actual fresh-ray validity and cell/block visits after the timed view submission.</summary>
    internal float[] TraceReceipts=>renderer.TraceReceipts?.ReadPixels()??[];
    /// <summary>Reads the successfully committed history metadata, independently of fresh-ray scheduling.</summary>
    internal float[] HistoryReceipts=>renderer.HistoryReceipts?.ReadPixels()??[];
    /// <summary>Requests the compiled receipt variant while keeping native composition unchanged.</summary>
    internal bool CaptureDiagnostics {get;set;}=true;
    /// <summary>Reads real contribution, update class, rejection and observation age outside submission.</summary>
    internal float[] HistoryDiagnostics=>renderer.HistoryDiagnostics?.ReadPixels()??[];
    /// <summary>Reads physical compact transport without native encoding or OIT expansion.</summary>
    internal float[] HistoryCloud=>renderer.CompletedHistory?.Images[0].ReadPixels()??[];
    /// <summary>Authors prior transport and confidence independently of the current trace.</summary>
    internal void SeedHistory(float[] cloud,float[] metadata,float[] diagnostics,float[]? bins=null)
    {
        var completed=renderer.CompletedHistory??throw new InvalidOperationException("History has not been drawn.");
        completed.Images[0].UploadDataImmediate(cloud);completed.Images[2].UploadDataImmediate(metadata);
        completed.Images[3].UploadDataImmediate(diagnostics.Where((_,i)=>i%4==3).ToArray());
        completed.Diagnostics?.UploadDataImmediate(diagnostics);
        if(bins is not null)completed.Images[1].UploadDataImmediate(bins);
    }
    /// <summary>Borrows opt-in query owners for a separate nonnested per-pass measurement.</summary>
    internal void SetPassTimers(GpuTimerQuery[]? timers){renderer.PassTimers=timers;}
    /// <summary>Reports the shared history admission used by the last view.</summary>
    internal bool HistoryCompatible=>renderer.HistoryCompatible;
    /// <summary>Authors the view-specific medium selector while fog remains owned by the shared frame snapshot.</summary>
    internal float Underwater {get;set;}
    internal AtmosphereLighting Atmosphere {get;set;}=new(Vector3.UnitZ,new Vector3(30),new Vector3(10),Vector3.Zero,Vector3.Zero,
        ImmutableArray<float>.Empty){Moon=-Vector3.UnitY,Lunar=new Vector3(3),CalibrationGeneration=7,LookupStorageScale=1f/16};
    #region Public API
    /// <summary>Allocates only existing GPU owners and the actual stored-volume view program.</summary>
    internal CloudStoredVolumeViewScene(ComponentShaderPrograms programs,int width=1,int height=1,
        Vector3 fogColor=default,float fogDensity=0,float fogMinimum=0)
    {
        try
        {
            camera=Own(TestFrameCamera.CreateFromProjection([1,0,0,0,0,1,0,0,0,0,-1.00020002f,-1,0,0,-.200020002f,0],width,height,.1f,1000,fogColor,fogDensity,fogMinimum));
            lighting=Own(new TestFrameLightingPublication());map=Own(RHICommandContext.Current.CreateDynamicTexture2D(new RHITextureDescription { Dimension = TextureDimension.Two, Format = TextureFormat.Rgba32f, Width = System.Math.Max(1, 20), Height = System.Math.Max(1, 20), MipLevels = 1, Storage = ResourceStorageMode.Mutable, Filter = TextureFilterMode.Nearest, BaseLevel = 0, MaxLevel = (1) - 1, Label = null }).RequireResource());
            material=Own(RHICommandContext.Current.CreateDynamicTexture2D(new RHITextureDescription { Dimension = TextureDimension.Two, Format = TextureFormat.R32f, Width = System.Math.Max(1, width), Height = System.Math.Max(1, height), MipLevels = 1, Storage = ResourceStorageMode.Mutable, Filter = TextureFilterMode.Nearest, BaseLevel = 0, MaxLevel = (1) - 1, Label = null }).RequireResource());visibility=Own(RHICommandContext.Current.CreateDynamicTexture2D(new RHITextureDescription { Dimension = TextureDimension.Two, Format = TextureFormat.R32f, Width = System.Math.Max(1, width), Height = System.Math.Max(1, height), MipLevels = 1, Storage = ResourceStorageMode.Mutable, Filter = TextureFilterMode.Nearest, BaseLevel = 0, MaxLevel = (1) - 1, Label = null }).RequireResource());
            liquid=Own(RHICommandContext.Current.CreateDynamicTexture2D(new RHITextureDescription { Dimension = TextureDimension.Two, Format = TextureFormat.R32f, Width = System.Math.Max(1, width), Height = System.Math.Max(1, height), MipLevels = 1, Storage = ResourceStorageMode.Mutable, Filter = TextureFilterMode.Nearest, BaseLevel = 0, MaxLevel = (1) - 1, Label = null }).RequireResource());shafts=Own(RHICommandContext.Current.CreateDynamicTexture2D(new RHITextureDescription { Dimension = TextureDimension.Two, Format = TextureFormat.R32f, Width = System.Math.Max(1, 1), Height = System.Math.Max(1, 1), MipLevels = 1, Storage = ResourceStorageMode.Mutable, Filter = TextureFilterMode.Nearest, BaseLevel = 0, MaxLevel = (1) - 1, Label = null }).RequireResource());
            shafts.UploadDataImmediate(new float[]{0});
            aerial=Own(RHICommandContext.Current.CreateDynamicTexture3D(new RHITextureDescription { Dimension = TextureDimension.Three, Format = TextureFormat.Rgba32f, Width = System.Math.Max(1, 1), Height = System.Math.Max(1, 2), Depth = System.Math.Max(1, 1), MipLevels = 1, Storage = ResourceStorageMode.Mutable, Filter = TextureFilterMode.Linear, BaseLevel = 0, MaxLevel = (1) - 1, Label = null }).RequireResource());
            attenuation=Own(RHICommandContext.Current.CreateDynamicTexture3D(new RHITextureDescription { Dimension = TextureDimension.Three, Format = TextureFormat.Rgba32f, Width = System.Math.Max(1, 1), Height = System.Math.Max(1, 1), Depth = System.Math.Max(1, 1), MipLevels = 1, Storage = ResourceStorageMode.Mutable, Filter = TextureFilterMode.Linear, BaseLevel = 0, MaxLevel = (1) - 1, Label = null }).RequireResource());
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
            Assert.True(VgeShaderPrograms.RegisterAll(programs.Api));
            var view=GpuShaderProgramLibraries.Get<CloudVolumeViewProgram>(programs.Api,CloudVolumeViewProgram.Contract.Identity);
            var reconstruction=GpuShaderProgramLibraries.Get<CloudVolumeReconstructProgram>(programs.Api,CloudVolumeReconstructProgram.Contract.Identity);
            Assert.NotNull(view);Assert.NotNull(reconstruction);
            Assert.True(reconstruction.EnsureReady(),reconstruction.PreparationFailure?.ToString());
            var temporal=GpuShaderProgramLibraries.Get<CloudVolumeTemporalProgram>(programs.Api,CloudVolumeTemporalProgram.Contract.Identity);
            Assert.NotNull(temporal);temporalProgram=temporal;renderer=Own(new CloudVolumeViewRenderer(view,reconstruction,temporal));
            output=Own(new CloudOitSurface(width,height,"Tests.Cloud.StoredVolume.View"));
            engineOutput=Own(DynamicGpuFramebuffer.Wrap(output.Target.FboId,"Tests.Engine.OIT"));
            engineOutput.RefreshWrappedFramebuffer(output.Target.FboId,width,height,publishPassMetadata:true);
        }
        catch{Dispose();throw;}
    }
    /// <summary>Authors authoritative density and a matching conservative occupancy field.</summary>
    internal void SeedDensity(float sigma)
    {
        var fields=storage.Published;var d=fields.Domain;
        fields.Density.UploadDataImmediate(Enumerable.Repeat(sigma,d.Voxels).ToArray(),0,0,0,d.Size,66,d.Size);
        fields.Occupancy.UploadData(Enumerable.Repeat(sigma==0?0u:1u,d.Blocks).ToArray());
    }
    /// <summary>Filters authored compact rays with explicit previous-camera rotation and material translation through the actual temporal program.</summary>
    internal float[][] FilterAuthored(float[] cloud,float[] bins,float[] metadata,bool diagnostics=true,Vector3 translation=default,float previousYaw=0)
    {
        temporalProgram.ConfigureOptions(()=>temporalProgram.DiagnosticsEnabled=diagnostics);
        Assert.True(temporalProgram.EnsureReady(),temporalProgram.PreparationFailure?.ToString());
        var previous=renderer.CompletedHistory??throw new InvalidOperationException("History has not been drawn.");
        using var raw=new CloudVolumeViewTargets(material.Width,material.Height,4);
        using var destination=new CloudVolumeViewTargets(material.Width,material.Height,2,true);
        destination.ConfigureDiagnostics(diagnostics);
        using var pass=new CloudVolumeViewPass(temporalProgram);
        using var geometry=new ArrayGraphicsGeometry(CloudVolumeViewRenderer.Layout,PrimitiveTopology.Triangles,
            new Dictionary<int,GpuVbo>(),proceduralVertices:3);
        using var noise=CloudBlueNoiseTexture.Create();noise.EnsureCreated();
        raw.Images[0].UploadDataImmediate(cloud);raw.Images[1].UploadDataImmediate(bins);raw.Images[2].UploadDataImmediate(metadata);
        var projection=camera.CurrentViewProjection.ToArray();
        if(previousYaw!=0)Vintagestory.API.MathTools.Mat4f.RotateY(projection,projection,previousYaw);
        // Author previous-camera state through the shared frame UBO, as both cloud and surface
        // reprojection now consume that owner rather than a second cloud projection matrix.
        using var authoredCamera=new VgeFrameUniformBuffer();
        var frameValues=System.Runtime.InteropServices.MemoryMarshal.Cast<byte,float>(camera.Bytes);
        authoredCamera.Capture(camera.Projection,frameValues.Slice(16,16),frameValues.Slice(32,16),frameValues.Slice(48,16),
            projection,camera.CurrentViewProjection,new(frameValues[96],frameValues[97]),frameValues[98],input.Frame,
            new(frameValues[100],frameValues[101],frameValues[102]),new(frameValues[104],frameValues[105],frameValues[106]),frameValues[107],
            new(frameValues[124],frameValues[125]),deltaTime:frameValues[126],fogMinimum:frameValues[103]);
        temporalProgram.Temporal.Capture(translation,destination.Width,destination.Height,input.Frame,true);
        lighting.Capture(1,input.Frame);
        try
        {
            temporalProgram.Frame=authoredCamera;temporalProgram.Lighting=lighting.Buffer;temporalProgram.BlueNoise=noise;
            temporalProgram.Cloud=raw.Images[0];temporalProgram.Bins=raw.Images[1];temporalProgram.Metadata=raw.Images[2];
            temporalProgram.PreviousCloud=previous.Images[0];temporalProgram.PreviousBins=previous.Images[1];
            temporalProgram.PreviousMetadata=previous.Images[2];temporalProgram.PreviousAge=previous.Images[3];temporalProgram.Diagnostics=default;
            temporalProgram.MaterialDepth=material;temporalProgram.VisibilityDepth=visibility;temporalProgram.LiquidDepth=liquid;
            pass.Prepare(destination.FilterPass,false);
            {
    var commands=RHICommandContext.Current;
    using var scope39=commands.EnterBoundary(new("Tests.Cloud.Temporal.Authored",true,graphics:System.Linq.Enumerable.Select<RHIGraphicsPipeline,RHIGraphicsPipelineRef>([pass.Pipeline!],value=>value),operations:BoundaryOperations.RenderPasses));
    scope39.Run(()=>pass.Draw(commands,geometry.Inputs));
}
            return [destination.Images[0].ReadPixels(),destination.Images[1].ReadPixels(),destination.Images[2].ReadPixels(),
                destination.Diagnostics?.ReadPixels()??[],destination.Images[3].ReadPixels()];
        }
        finally
        {
            temporalProgram.Frame=null;temporalProgram.Lighting=null;temporalProgram.BlueNoise=default;
            temporalProgram.Cloud=default;temporalProgram.Bins=default;temporalProgram.Metadata=default;
            temporalProgram.PreviousCloud=default;temporalProgram.PreviousBins=default;temporalProgram.PreviousMetadata=default;temporalProgram.PreviousAge=default;temporalProgram.Diagnostics=default;
            temporalProgram.MaterialDepth=default;temporalProgram.VisibilityDepth=default;temporalProgram.LiquidDepth=default;
        }
    }
    /// <summary>Authors independent material, particle and liquid depth clips in projection space.</summary>
    internal void SetDepths(float opaque,float particles,float water)
    {
        int count=material.Width*material.Height;
        material.UploadDataImmediate(Enumerable.Repeat(Depth(opaque),count).ToArray());
        visibility.UploadDataImmediate(Enumerable.Repeat(Depth(particles),count).ToArray());
        liquid.UploadDataImmediate(Enumerable.Repeat(Depth(water),count).ToArray());
    }
    /// <summary>Gives every full-resolution texel a distinct clip depth to certify compact trace addressing.</summary>
    internal void SetMaterialDepthRamp()
    {
        var depths=Enumerable.Range(0,material.Width*material.Height).Select(i=>.2f+i*.001f).ToArray();
        material.UploadDataImmediate(depths);
    }
    /// <summary>Authors a material-depth discontinuity while keeping particle and liquid clips independent.</summary>
    internal void SetMaterialDepthColumns(int firstBackground,float foreground,float background)
    {
        var depths=new float[material.Width*material.Height];
        for(int y=0;y<material.Height;y++)for(int x=0;x<material.Width;x++)
            depths[y*material.Width+x]=Depth(x<firstBackground?foreground:background);
        material.UploadDataImmediate(depths);
    }
    /// <summary>Draws actual typed OIT, optionally advancing the complete independent stored-volume update chain.</summary>
    internal float[][] Draw(CloudVolumeViewSettings settings,float scale=1,CloudVolumeSolution? solution=null,bool succeeds=true,float? clear=null)
    {
        if(solution is not null)
        {
            solution.Prepare(settings);solution.Admission.Observe(Atmosphere);
            // The fixture explicitly waits between simulated frames; production only polls zero-timeout fences.
            uint before=solution.Drawable?.Revision??0;
            for(int attempt=0;attempt<64;attempt++)
            {
                var observation=input with{Frame=++updateFrame};solution.Poll(observation);
                Assert.Null(solution.Failure);
                if(solution.Drawable is {} publication&&publication.Revision>before)break;
                var retained=solution.Drawable;
                solution.Submit(observation,map,Atmosphere,camera,settings,()=>true);Assert.Null(solution.Failure);
                Assert.Same(retained,solution.Drawable); // Submission alone cannot publish unfinished writes.
                using var completed=RHICommandContext.Current.InsertFence();
                Assert.Contains(RHICommandContext.Current.WaitCpu(completed, TimeSpan.FromSeconds(1)),new[]{CompletionStatus.Ready,CompletionStatus.Ready});
            }
            Assert.NotNull(solution.Drawable);Assert.True(solution.Drawable.Revision>before);
        }
        PrepareDraw(settings,scale,clear);
        bool drawn=solution is null?DrawOnly():renderer.Draw(solution.Drawable!,input,preparedScene,engineOutput,()=>true,captureDiagnostics:CaptureDiagnostics);
        if(succeeds)Assert.True(drawn,renderer.Failure);else{Assert.False(drawn);Assert.NotNull(renderer.Failure);}
        return output.Images.Select(texture=>texture.ReadPixels()).ToArray();
    }
    /// <summary>Submits borrowed inputs through the real typed view and verifies restored native selections.</summary>
    internal float[][] DrawBorrowedInputs(CloudVolumeViewSettings settings)
    {
        using var borrowedMap=new BorrowedTexture(map.TextureId);
        using var borrowedMaterial=new BorrowedTexture(material.TextureId);
        using var borrowedVisibility=new BorrowedTexture(visibility.TextureId);
        using var borrowedLiquid=new BorrowedTexture(liquid.TextureId);
        Assert.Equal(input.Storage.Map,borrowedMap.TextureId);
        Assert.Equal(20,borrowedMap.Width);
        PrepareDraw(settings);
        preparedScene=new CloudVolumeViewScene(camera,lighting.Buffer,Atmosphere,borrowedMaterial,borrowedVisibility,borrowedLiquid,aerial,attenuation,shafts,Underwater);
        int draw=GL.GetInteger(GetPName.DrawFramebufferBinding),read=GL.GetInteger(GetPName.ReadFramebufferBinding);
        int unit=GL.GetInteger(GetPName.ActiveTexture),texture=GL.GetInteger(GetPName.TextureBinding2D);
        Assert.True(DrawOnly());
        Assert.Equal(draw,GL.GetInteger(GetPName.DrawFramebufferBinding));
        Assert.Equal(read,GL.GetInteger(GetPName.ReadFramebufferBinding));
        Assert.Equal(unit,GL.GetInteger(GetPName.ActiveTexture));
        Assert.Equal(texture,GL.GetInteger(GetPName.TextureBinding2D));
        return output.Images.Select(image=>image.ReadPixels()).ToArray();
    }
    /// <summary>Prepares immutable view inputs and clears targets outside the measured draw interval.</summary>
    internal void PrepareDraw(CloudVolumeViewSettings settings,float scale=1,float? clear=null)
    {
        lighting.Capture(scale,input.Frame);Assert.True(renderer.Prepare(settings),renderer.Failure);
        RHIColorAttachment[] colors=Enumerable.Range(0,6).Select(i=>new RHIColorAttachment(AttachmentIndex:i,Load:AttachmentLoad.Clear,
            Clear:clear is {} c?ColorClearValue.Float(c,c,c,c):i<2?ColorClearValue.Float(1,1,1,1):ColorClearValue.Float(0,0,0,0))).ToArray();
        {
    var commands=RHICommandContext.Current;
    using var scope40=commands.EnterBoundary(new("Tests.Cloud.StoredVolume.Clear",true,graphics:System.Linq.Enumerable.Select<RHIGraphicsPipeline,RHIGraphicsPipelineRef>([],value=>value),operations:BoundaryOperations.RenderPasses));
    scope40.Run(()=>{commands.BeginRenderPass(FramebufferTestSetup.DescribeAttachments(output.Target, colors));commands.EndRenderPass();});
}
        preparedScene=new CloudVolumeViewScene(camera,lighting.Buffer,Atmosphere,material,visibility,liquid,aerial,attenuation,shafts,Underwater);
    }
    /// <summary>Submits only the actual typed view draw, without preparation, clearing or readback.</summary>
    internal bool DrawOnly()=>replacementIdentity is null?renderer.Draw(storage,input,preparedScene,engineOutput,()=>true,captureDiagnostics:CaptureDiagnostics):
        renderer.Draw(storage.Published.View with{MaterialIdentity=replacementIdentity},input,preparedScene,engineOutput,()=>true,captureDiagnostics:CaptureDiagnostics);
    /// <summary>Models a new material owner reusing identical drawable allocations.</summary>
    internal void ReplaceViewMaterialIdentity(){replacementIdentity=new object();}
    /// <summary>Advances a complete camera/native frame pair without changing cloud storage identity.</summary>
    internal void AdvanceFrame(float materialMotion=0,bool cameraCut=false,float yaw=0,uint? absoluteFrame=null)
    {
        var bytes=camera.Bytes.ToArray();uint frame=absoluteFrame??unchecked(input.Frame+1);
        System.Buffers.Binary.BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(396,4),frame);
        System.Buffers.Binary.BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(508,4),cameraCut?1u:0u);
        var v=System.Runtime.InteropServices.MemoryMarshal.Cast<byte,float>(bytes.AsSpan());
        if(yaw!=0)
        {
            var view=v.Slice(16,16).ToArray();var inverse=new float[16];var clip=new float[16];
            Vintagestory.API.MathTools.Mat4f.RotateY(view,view,yaw);
            Vintagestory.API.MathTools.Mat4f.Invert(inverse,view);
            Vintagestory.API.MathTools.Mat4f.Mul(clip,v[..16].ToArray(),view);
            v.Slice(80,16).CopyTo(v.Slice(64,16));view.CopyTo(v.Slice(16,16));
            inverse.CopyTo(v.Slice(48,16));clip.CopyTo(v.Slice(80,16));
        }
        // Recapture the retained camera rather than decoding calibration into a new owner: optional
        // accessor defaults otherwise become explicit values and spuriously advance its generation.
        camera.Capture(v[..16],v.Slice(16,16),v.Slice(32,16),v.Slice(48,16),v.Slice(64,16),v.Slice(80,16),
            new(v[96],v[97]),v[98],frame,new(v[100],v[101],v[102]),new(v[104],v[105],v[106]),v[107],
            new(v[124],v[125]),deltaTime:v[126],cameraCut:cameraCut,fogMinimum:v[103]);
        input=input with{Frame=frame,
            Placement=input.Placement with{Offset=input.Placement.Offset+new Vector3(materialMotion,0,0)}};
    }
    /// <summary>Replaces storage behind the same retained image owner to exercise target realization invalidation.</summary>
    internal void RefreshOutputMetadata()
    {
        int width=output.Images[0].Width,height=output.Images[0].Height;
        Assert.True(output.Images[0].Resize(width+1,height));
        Assert.True(output.Images[0].Resize(width,height));
        using var selection=StateCache.Current.BindFramebufferScope();
        output.Target.Bind();
        engineOutput.RefreshWrappedFramebuffer(output.Target.FboId,width,height,publishPassMetadata:true);
    }
    /// <summary>Exercises the same screen-resource retirement boundary used by client resize and reload.</summary>
    internal void ReleaseViewTargets()
    {
        renderer.ReleaseTargets();Assert.Equal(0,renderer.TargetBytes);
    }
    /// <summary>Leaves a real update pending, optionally retiring and resubmitting in the same simulated render callback.</summary>
    internal void QueueUpdate(CloudVolumeSolution solution,CloudVolumeViewSettings settings,bool retireAndResubmit=false)
    {
        var observation=input with{Frame=++updateFrame};solution.Poll(observation);
        var retained=solution.Drawable;solution.Submit(observation,map,Atmosphere,camera,settings,()=>true);
        Assert.Null(solution.Failure);Assert.True(solution.UpdatePending);Assert.Same(retained,solution.Drawable);
        if(!retireAndResubmit)return;
        using var completed=RHICommandContext.Current.InsertFence();
        Assert.Contains(RHICommandContext.Current.WaitCpu(completed, TimeSpan.FromSeconds(5)),new[]{CompletionStatus.Ready,CompletionStatus.Ready});
        observation=observation with{Frame=++updateFrame};solution.Poll(observation);
        Assert.Null(solution.Failure);Assert.False(solution.UpdatePending);
        Assert.True(solution.Drawable!.Revision>retained!.Revision);
        retained=solution.Drawable;
        // This is the ordinary Poll/draw/Submit ordering: retiring a chain must not insert an idle callback.
        solution.Submit(observation,map,Atmosphere,camera,settings,()=>true);
        Assert.Null(solution.Failure);Assert.True(solution.UpdatePending);Assert.Same(retained,solution.Drawable);
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
