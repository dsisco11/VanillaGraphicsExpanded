using System;
using VanillaGraphicsExpanded.ModSystems;
using VanillaGraphicsExpanded.PBR.Atmosphere;
using VanillaGraphicsExpanded.PBR.Liquids;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Rendering.Integration;
using VanillaGraphicsExpanded.Rendering.Pipeline;
using VanillaGraphicsExpanded.Rendering.Pipeline.Descriptions;
using VanillaGraphicsExpanded.Rendering.Pipeline.Passes;
using VanillaGraphicsExpanded.Rendering.Shaders;
using Vintagestory.API.Client;

namespace VanillaGraphicsExpanded.PBR.Clouds;

/// <summary>Owns reduced tracing, rotating physical history and depth-aware submission to existing native OIT.</summary>
internal sealed class CloudVolumeReconstruction : IDisposable
{
    private readonly ICoreClientAPI api;
    private readonly CloudTemporalHistory history=new();
    private readonly GraphicsPipelineLifetime lifetime=new();
    private CloudVolumeStorage? storage;
    private CloudVolumeStorage? traceStorage;
    private CloudResolutionLayout resolution;
    private CloudTraceProgram? traceProgram;
    private CloudTemporalProgram? temporalProgram;
    private CloudResolveProgram? resolveProgram;
    private GraphicsPipeline? resolvePipeline;
    private int width,height;
    private int cursor;
    private CloudBlueNoiseTexture? primaryNoise;
    private bool disposed;
    private readonly CloudShadowCache shadowCache;

    #region Public API
    #region Preparation and admission
    /// <summary>Uses the existing shader library and leaves registration/weather ownership with its caller.</summary>
    internal CloudVolumeReconstruction(ICoreClientAPI api,Func<CloudShadowAttenuationComputeShader>? createShadowCache=null)
    {
        this.api=api;
        shadowCache=new(createShadowCache??(()=>CloudShadowAttenuationComputeShader.Create(api)));
    }
    /// <summary>Requires all reduced-path executables before the default draw can take ownership.</summary>
    internal bool Prepare(bool selfShadowing=true)
    {
        traceProgram=GpuShaderProgramLibraries.Get<CloudTraceProgram>(api,"cloud_volume_trace");
        temporalProgram=GpuShaderProgramLibraries.Get<CloudTemporalProgram>(api,"cloud_volume_temporal");
        resolveProgram=GpuShaderProgramLibraries.Get<CloudResolveProgram>(api,"cloud_volume_resolve");
        if(traceProgram is not null)traceProgram.ConfigureOptions(()=>traceProgram.SelfShadowing=selfShadowing);
        if(traceProgram?.EnsureReady()!=true || temporalProgram?.EnsureReady()!=true || resolveProgram?.EnsureReady()!=true) return false;
        primaryNoise ??= CloudBlueNoiseTexture.Create();
        primaryNoise.EnsureCreated();
        traceProgram.PrimaryNoise=primaryNoise;
        return true;
    }
    /// <summary>Rejects content history and withdraws attenuation while retaining correctly sized history allocations.</summary>
    internal void Invalidate() { history.Invalidate();shadowCache.Reset(); }
    #endregion
    #region Submission
    /// <summary>Runs complete payload passes under one owned graphics boundary; commits history only after successful OIT submission.</summary>
    internal bool Draw(CloudMapInput input,AtmosphereLighting lighting,CloudVolumeSettings settings,GpuFramebuffer target,
        int sceneDepth,int visibilityDepth,int liquidDepth,ArrayGraphicsGeometry geometry,Func<bool> current,CloudHierarchy? hierarchy=null)
    {
        if(traceProgram is null||temporalProgram is null||resolveProgram is null) { Invalidate(); return false; }
        try
        {
            // Select and admit the requested executable before constructing pass identities.
            // A failed variant switch must not draw the previously installed lighting policy.
            traceProgram.ConfigureOptions(()=>traceProgram.SelfShadowing=settings.ShadowQuality>0);
            if(!traceProgram.EnsureReady()) { Invalidate(); return false; }
            EnsureSize(api.Render.FrameWidth,api.Render.FrameHeight,settings.TraceScale);
            bool quarter=settings.TraceScale==CloudTraceScale.Quarter;
            var trace=quarter?traceStorage!.Blocks[0]:storage!.Blocks[cursor];
            var previous=storage!.Blocks[quarter?cursor:(cursor+1)%3];
            var next=storage.Blocks[quarter?1-cursor:(cursor+2)%3];
            var camera=VgeFrameRenderer.Current;
            Capture(traceProgram,traceProgram.Parameters,input,lighting,settings,sceneDepth,visibilityDepth,liquidDepth);
            traceProgram.SetShadowCache(null,0);
            traceProgram.PrimaryNoise=primaryNoise;
            temporalProgram.SceneDepth=sceneDepth; temporalProgram.VisibilityDepth=visibilityDepth; temporalProgram.LiquidDepth=liquidDepth;
            resolveProgram.SceneDepth=sceneDepth; resolveProgram.VisibilityDepth=visibilityDepth; resolveProgram.LiquidDepth=liquidDepth;
            // Lifecycle and transport-mode certificates identify compatibility. Normal atmospheric
            // publications and native map generations evolve without discarding accumulation.
            foreach(var temporal in new[]{traceProgram.Temporal,temporalProgram.Temporal,resolveProgram.Temporal})
                history.Capture(input,camera,settings,traceProgram.Parameters,width,height,temporal);
            temporalProgram.HistoryPayload=previous.View; resolveProgram.HistoryPayload=next.View;
            temporalProgram.CurrentPayload=trace.View;
            var tracePass=trace.Prepare(lifetime,traceProgram); var temporalPass=next.Prepare(lifetime,temporalProgram);
            var pass=new RenderPassDesc(target,LiquidPipelineStates.SurfaceOutputs);
            using var metadata=new RenderPassTargets(pass);
            var description=new GraphicsPipelineDesc(resolveProgram.GraphicsIdentity!,CloudVolumeRenderer.Layout,metadata.Signature,
                DynamicPipelineState.Viewport,blending:LiquidPipelineStates.SurfaceBlending);
            if(resolvePipeline is null||resolvePipeline.Description!=description||!ReferenceEquals(resolvePipeline.Shader,resolveProgram)
                ||resolvePipeline.ExecutableRevision!=resolveProgram.ExecutableRevision)
            {
                var replacement=new GraphicsPipeline(lifetime,description,resolveProgram); resolvePipeline?.Dispose(); resolvePipeline=replacement;
            }
            if(!current()) { Invalidate(); return false; }
            // An unavailable summary uses native support checks at the same scheduled shadow points.
            DynamicTexture2D? bounds=null;
            if(hierarchy is not null) hierarchy.TryGetCurrent(input,out bounds);
            traceProgram.SetHierarchy(bounds);
            traceProgram.SetShadowCache(shadowCache.Build(input,lighting,traceProgram.Parameters,settings.ShadowMapResolution,bounds,current,out int source),source);
            if(!current()) { Invalidate(); return false; }
            if(!GraphicsCommandContext.TryRun("Cloud.Volume.Temporal",[tracePass.Pipeline,temporalPass.Pipeline,resolvePipeline],true,commands=>
            {
                foreach(var item in new[]{tracePass,temporalPass,(Pass:pass,Pipeline:resolvePipeline)})
                {
                    commands.BeginPass(item.Pass); commands.SetPipeline(item.Pipeline);
                    commands.SetDynamicState(new(){Viewport=commands.PassViewport}); commands.Draw(geometry,new(0,3)); commands.EndPass();
                }
            })) { Invalidate(); throw new InvalidOperationException("Cloud reconstruction graphics boundary was rejected."); }
            history.Commit(input,camera,settings,traceProgram.Parameters);
            // Rotate only after commit: Half reuses the discarded block for trace; Quarter alternates histories.
            cursor=quarter?1-cursor:(cursor+1)%3; return true;
        }
        catch { Invalidate(); throw; }
    }
    #endregion
    #region Resource lifecycle
    /// <summary>Expires executable references and owned targets at resize/reload boundaries.</summary>
    internal void Reset()
    {
        Invalidate(); traceProgram=null; temporalProgram=null; resolveProgram=null;
        RenderingCleanup.Run(()=>{resolvePipeline?.Dispose();resolvePipeline=null;},ReleaseTargets,
            ()=>{primaryNoise?.Dispose();primaryNoise=null;});
    }
    /// <summary>Releases only VGE resources; current native column and scene textures remain borrowed.</summary>
    public void Dispose()
    {
        if(disposed)return;
        disposed=true;
        RenderingCleanup.Run(Reset,shadowCache.Dispose,lifetime.Dispose);
    }
    #endregion
    #endregion

    #region Private
    /// <summary>Replaces all history sets atomically on size changes; ordinary frames reuse persistent storage.</summary>
    private void EnsureSize(int viewWidth,int viewHeight,CloudTraceScale scale)
    {
        var size=CloudResolutionLayout.Create(viewWidth,viewHeight,scale);
        if(storage is not null && resolution==size) return;
        CloudVolumeStorage? replacement=null,newTrace=null;
        try
        {
            replacement=new CloudVolumeStorage(size.Width,size.Height,"Cloud.Volume.History",scale==CloudTraceScale.Quarter?2:3);
            if(scale==CloudTraceScale.Quarter)
                newTrace=new CloudVolumeStorage(size.TraceWidth,size.TraceHeight,"Cloud.Volume.Trace",1);
            // All new allocations exist before retiring any old view or its cached pipeline.
            ReleaseTargets();
        }
        catch
        {
            RenderingCleanup.Run(()=>replacement?.Dispose(),()=>newTrace?.Dispose());
            throw;
        }
        storage=replacement;traceStorage=newTrace;resolution=size;
        width=size.Width;height=size.Height;cursor=0;Invalidate();
    }
    /// <summary>Retires every block before its shared allocation and forgets allocation dimensions and roles.</summary>
    private void ReleaseTargets()
    {
        var oldHistory=storage;var oldTrace=traceStorage;
        storage=traceStorage=null;resolution=default;width=height=cursor=0;
        RenderingCleanup.Run(()=>oldHistory?.Dispose(),()=>oldTrace?.Dispose());
    }
    /// <summary>Captures physical density and lighting inputs only for reduced tracing.</summary>
    private void Capture(ICloudVolumeBindings program,CloudVolumeParameters parameters,CloudMapInput input,AtmosphereLighting lighting,
        CloudVolumeSettings settings,int sceneDepth,int visibilityDepth,int liquidDepth)
    {
        parameters.Capture(api,input,lighting,settings); program.Map=input.Storage.Map; program.SceneDepth=sceneDepth; program.VisibilityDepth=visibilityDepth; program.LiquidDepth=liquidDepth;
        program.AerialRadiance=AtmosphereModSystem.AerialRadianceTexture; program.AerialAttenuation=AtmosphereModSystem.AerialAttenuationTexture;
        program.LightShafts=Postprocessing.LightShaftOcclusionRenderer.Texture;
    }
    #endregion
}
