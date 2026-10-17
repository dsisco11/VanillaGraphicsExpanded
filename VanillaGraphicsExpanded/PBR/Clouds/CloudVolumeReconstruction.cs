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

/// <summary>Owns reduced tracing, ping-pong physical history and depth-aware submission to existing native OIT.</summary>
internal sealed class CloudVolumeReconstruction : IDisposable
{
    private readonly ICoreClientAPI api;
    private readonly CloudTemporalHistory history=new();
    private readonly GraphicsPipelineLifetime lifetime=new();
    private CloudVolumeSurface? trace,first,second;
    private CloudTraceProgram? traceProgram;
    private CloudTemporalProgram? temporalProgram;
    private CloudResolveProgram? resolveProgram;
    private GraphicsPipeline? resolvePipeline;
    private int width,height;
    private bool swap;
    private CloudPmjJitterTexture? pmjJitter;

    #region Public API
    #region Preparation and admission
    /// <summary>Uses the existing shader library and leaves registration/weather ownership with its caller.</summary>
    internal CloudVolumeReconstruction(ICoreClientAPI api) { this.api=api; }
    /// <summary>Requires all reduced-path executables before the default draw can take ownership.</summary>
    internal bool Prepare()
    {
        traceProgram=GpuShaderProgramLibraries.Get<CloudTraceProgram>(api,"cloud_volume_trace");
        temporalProgram=GpuShaderProgramLibraries.Get<CloudTemporalProgram>(api,"cloud_volume_temporal");
        resolveProgram=GpuShaderProgramLibraries.Get<CloudResolveProgram>(api,"cloud_volume_resolve");
        if(traceProgram?.EnsureReady()!=true || temporalProgram?.EnsureReady()!=true || resolveProgram?.EnsureReady()!=true) return false;
        pmjJitter ??= CloudPmjJitterTexture.Create();
        pmjJitter.EnsureCreated();
        traceProgram.PmjJitter=pmjJitter;
        return true;
    }
    /// <summary>Rejects content history immediately while retaining correctly sized owned allocations.</summary>
    internal void Invalidate() { history.Invalidate(); }
    #endregion
    #region Submission
    /// <summary>Runs complete payload passes under one owned graphics boundary; commits history only after successful OIT submission.</summary>
    internal bool Draw(CloudMapInput input,AtmosphereLighting lighting,CloudVolumeSettings settings,GpuFramebuffer target,
        int sceneDepth,int visibilityDepth,int liquidDepth,ArrayGraphicsGeometry geometry,Func<bool> current,CloudHierarchy? hierarchy=null)
    {
        if(traceProgram is null||temporalProgram is null||resolveProgram is null) return false;
        EnsureSize(api.Render.FrameWidth,api.Render.FrameHeight);
        var previous=swap?second!:first!; var next=swap?first!:second!;
        var camera=VgeFrameRenderer.Current;
        Capture(traceProgram,traceProgram.Parameters,input,lighting,settings,sceneDepth,visibilityDepth,liquidDepth);
        traceProgram.PmjJitter=pmjJitter;
        resolveProgram.SceneDepth=sceneDepth; resolveProgram.VisibilityDepth=visibilityDepth; resolveProgram.LiquidDepth=liquidDepth;
        // The immutable publication and native content certificates identify compatibility. New
        // map generation numbers alone do not: the native producer legitimately writes every frame.
        foreach(var temporal in new[]{traceProgram.Temporal,temporalProgram.Temporal,resolveProgram.Temporal})
            history.Capture(input,camera,settings,lighting,traceProgram.Parameters,width,height,temporal);
        BindHistory(temporalProgram,previous); BindHistory(resolveProgram,next);
        temporalProgram.Current0=trace!.Images[0].TextureId; temporalProgram.Current1=trace.Images[1].TextureId;
        temporalProgram.Current2=trace.Images[2].TextureId; temporalProgram.Current3=trace.Images[3].TextureId;
        temporalProgram.Current4=trace.Images[4].TextureId; temporalProgram.Current5=trace.Images[5].TextureId; temporalProgram.Current6=trace.Images[6].TextureId;
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
        // A current map can still outlive an unavailable summary. Explicitly select DDA in that case.
        traceProgram.SetHierarchy(hierarchy is not null && hierarchy.TryGetCurrent(input,out var bounds)?bounds:null);
        if(!GraphicsCommandContext.TryRun("Cloud.Volume.Temporal",[tracePass.Pipeline,temporalPass.Pipeline,resolvePipeline],true,commands=>
        {
            foreach(var item in new[]{tracePass,temporalPass,(Pass:pass,Pipeline:resolvePipeline)})
            {
                commands.BeginPass(item.Pass); commands.SetPipeline(item.Pipeline);
                commands.SetDynamicState(new(){Viewport=commands.PassViewport}); commands.Draw(geometry,new(0,3)); commands.EndPass();
            }
        })) { Invalidate(); throw new InvalidOperationException("Cloud reconstruction graphics boundary was rejected."); }
        history.Commit(input,camera,settings,lighting,traceProgram.Parameters);
        swap=!swap; return true;
    }
    #endregion
    #region Resource lifecycle
    /// <summary>Expires executable references and owned targets at resize/reload boundaries.</summary>
    internal void Reset()
    {
        Invalidate(); traceProgram=null; temporalProgram=null; resolveProgram=null;
        RenderingCleanup.Run(()=>{resolvePipeline?.Dispose();resolvePipeline=null;},ReleaseTargets,
            ()=>{pmjJitter?.Dispose();pmjJitter=null;});
    }
    /// <summary>Releases only VGE resources; current native column and scene textures remain borrowed.</summary>
    public void Dispose() { RenderingCleanup.Run(Reset,lifetime.Dispose); }
    #endregion
    #endregion

    #region Private
    /// <summary>Replaces all history sets atomically on size changes; ordinary frames reuse persistent storage.</summary>
    private void EnsureSize(int viewWidth,int viewHeight)
    {
        var size=CloudVolumeSurface.Dimensions(viewWidth,viewHeight);
        if(trace is not null && width==size.Width && height==size.Height) return;
        CloudVolumeSurface? a=null,b=null,c=null;
        try
        {
            a=new(size.Width,size.Height,"Cloud.Trace"); b=new(size.Width,size.Height,"Cloud.History.A"); c=new(size.Width,size.Height,"Cloud.History.B");
        }
        catch { RenderingCleanup.Run(()=>a?.Dispose(),()=>b?.Dispose(),()=>c?.Dispose()); throw; }
        ReleaseTargets(); trace=a; first=b; second=c; width=size.Width; height=size.Height; swap=false; Invalidate();
    }
    /// <summary>Retires pipelines before their owned backing images and forgets allocation dimensions.</summary>
    private void ReleaseTargets()
    {
        RenderingCleanup.Run(()=>{trace?.Dispose();trace=null;},()=>{first?.Dispose();first=null;},()=>{second?.Dispose();second=null;});
        width=height=0;
    }
    /// <summary>Binds the complete native-compatible payload; all images belong to an alternate non-writing set.</summary>
    private static void BindHistory(ICloudHistoryBindings program,CloudVolumeSurface surface)
    {
        program.History0=surface.Images[0].TextureId; program.History1=surface.Images[1].TextureId;
        program.History2=surface.Images[2].TextureId; program.History3=surface.Images[3].TextureId;
        program.History4=surface.Images[4].TextureId; program.History5=surface.Images[5].TextureId; program.History6=surface.Images[6].TextureId;
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
