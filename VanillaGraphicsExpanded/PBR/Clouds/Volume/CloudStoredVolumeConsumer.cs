using System;
using System.Numerics;
using VanillaGraphicsExpanded.ModSystems;
using VanillaGraphicsExpanded.PBR.Atmosphere;
using VanillaGraphicsExpanded.PBR.CameraExposure;
using VanillaGraphicsExpanded.PBR.SceneColor;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Rendering.Integration;
using VanillaGraphicsExpanded.Rendering.Shaders;
using Vintagestory.API.Client;

namespace VanillaGraphicsExpanded.PBR.Clouds.Volume;

/// <summary>Adapts stored-volume rendering to native frame/lifecycle contracts without owning native weather or textures.</summary>
internal sealed class CloudStoredVolumeConsumer : ICloudVolumeConsumer
{
    private readonly ICoreClientAPI api;
    private readonly CloudNativeRuntime runtime;
    private readonly Func<CloudVolumeViewSettings> selection;
    private readonly Action unregisterResize;
    private CloudVolumeSolution? solution;
    private CloudVolumeViewRenderer? view;
    private GpuFramebuffer? target;
    private CloudVolumeViewSettings settings;
    private AtmosphereLighting? admitted;
    private bool prepared,failed,disposed;
    internal string DiagnosticText=>$"{CloudStoredVolumeDiagnostics.Describe(solution,failed)}\nLighting ray budget per update: {settings.LightingRaysPerUpdate:N0}\nView targets: {view?.TargetBytes??0:N0} bytes (trace divisor {settings.TraceDivisor}; full resolution draws directly).";

    #region Public API
    #region Admission
    /// <summary>Draws after native generation without advancing weather.</summary>
    public double RenderOrder=>.31;
    /// <summary>Cloud admission is independent of terrain chunk visibility.</summary>
    public int RenderRange=>int.MaxValue;
    /// <summary>Registers only resize metadata retirement; visible callback admission remains native-runtime owned.</summary>
    internal CloudStoredVolumeConsumer(ICoreClientAPI api,CloudNativeRuntime runtime,Func<CloudVolumeViewSettings> selection)
    {
        this.api=api;this.runtime=runtime;this.selection=selection;
        unregisterResize=ScreenResourceManager.Register(ScreenResourceManager.CompositeOrder,RetireTarget);
    }
    /// <summary>Applies cached cloud controls before Opaque while preserving compatible fields for view-only changes.</summary>
    public bool Prepare()
    {
        if(disposed || failed || AtmosphereModSystem.Lighting is not { } lighting || lighting.CalibrationGeneration==0 || !GpuUniformRingSystem.TryGetCurrent(out _)) return false;
        var next=selection();
        try
        {
            if(prepared && next==settings) return true;
            bool transport=solution is not null && (next.Detail!=settings.Detail || next.SelfShadowing!=settings.SelfShadowing || next.Diffuse!=settings.Diffuse);
            if(transport) {if(next.Detail!=settings.Detail){solution!.Dispose();solution=null;}else solution!.ReconfigureLighting();}
            settings=next;
            solution??=new(api,settings.Detail);solution.Prepare(settings);
            solution.Admission.Observe(lighting);
            if(view is null)
            {
                var program=GpuShaderProgramLibraries.Get<CloudVolumeViewProgram>(api,CloudVolumeViewProgram.Contract.Identity);
                var reconstruction=GpuShaderProgramLibraries.Get<CloudVolumeReconstructProgram>(api,CloudVolumeReconstructProgram.Contract.Identity);
                var temporal=GpuShaderProgramLibraries.Get<CloudVolumeTemporalProgram>(api,CloudVolumeTemporalProgram.Contract.Identity);
                if(program is null||reconstruction is null||temporal is null) return false;
                view=new(program,reconstruction,temporal);
            }
            if(!view.Prepare(settings)) throw new InvalidOperationException(view.Failure);
            prepared=true;return true;
        }
        catch(Exception error) when(!EngineBoundaryRestoreException.IsRestorationFailure(error))
        {Fail(error);return false;}
    }
    /// <summary>Admits lifetime source envelopes before Opaque; failure withdraws this consumer rather than admitting unbounded light.</summary>
    public float Maximum(AtmosphereLighting lighting,float localFogMaximum)
    {
        if(failed || !prepared || solution is null) return 0;
        try {float maximum=solution.Admission.Observe(lighting,localFogMaximum);admitted=lighting;return maximum;}
        catch(Exception error) {Fail(error);return 0;}
    }
    #endregion
    #region Submission
    /// <summary>Draws the completed snapshot, then submits one bounded future update without waiting for its completion.</summary>
    public void OnRenderFrame(float deltaTime,EnumRenderStage stage)
    {
        runtime.Update();
        if(disposed || failed || !prepared || stage!=EnumRenderStage.OIT || !runtime.Ownership.Owns(CloudMode.Volumetric) ||
            !runtime.TryGetMap(out var input)) return;
        var render=api.Render;var buffers=render.FrameBuffers;
        if(render.FrameWidth<=0 || render.FrameHeight<=0 || buffers.Count<=(int)EnumFrameBuffer.Transparent ||
            buffers.Count<=(int)EnumFrameBuffer.Primary || buffers.Count<=(int)EnumFrameBuffer.LiquidDepth) return;
        var transparent=buffers[(int)EnumFrameBuffer.Transparent];var primary=buffers[(int)EnumFrameBuffer.Primary];
        var liquid=buffers[(int)EnumFrameBuffer.LiquidDepth];
        if(transparent is null || primary is null || liquid is null || primary.DepthTextureId==0 || liquid.DepthTextureId==0 ||
            render.CurrentRenderStage!=EnumRenderStage.OIT || render.CurrentFrameBuffer?.FboId!=transparent.FboId ||
            !DepthHierarchyRenderer.TryGetCurrent(VgeFrameRenderer.Current,render.FrameWidth,render.FrameHeight,out var depth) || depth is null ||
            AtmosphereModSystem.Lighting is not { } lighting || lighting.CalibrationGeneration==0 || !ReferenceEquals(lighting,admitted) || AtmosphereModSystem.AerialRadianceTexture is not { } aerial ||
            AtmosphereModSystem.AerialAttenuationTexture is not { } attenuation) return;
        try
        {
            SceneLightingPublicationRenderer.RequireNativeInputs("cloud_volume");
            bool Current()=>runtime.TryGetMap(out var current) && current==input && ReferenceEquals(lighting,AtmosphereModSystem.Lighting);
            // Borrow only for synchronous submissions. No native map wrapper survives this callback.
            using var map=new BorrowedTexture(input.Storage.Map);
            solution!.Poll(input);
            if(solution.Failure is not null)throw new InvalidOperationException(solution.Failure);
            if(solution.Drawable is {} publication)
            {
            using var material=new BorrowedTexture(depth.TextureId);
            using var visibility=new BorrowedTexture(primary.DepthTextureId);
            using var liquidDepth=new BorrowedTexture(liquid.DepthTextureId);
            var shafts=Postprocessing.LightShaftOcclusionRenderer.Texture;
            var scene=new CloudVolumeViewScene(VgeFrameRenderer.Current,CameraExposureRenderer.FrozenLighting,lighting,
                material,visibility,liquidDepth,aerial,attenuation,shafts,render.ShaderUniforms.CameraUnderwater,
                new(api.Ambient.BlendedFlatFogDensity,
                    api.Ambient.BlendedFlatFogYPosForShader-(float)api.World.Player.Entity.CameraPos.Y));
            if(target is null || target.FboId!=transparent.FboId || target.Width!=transparent.Width || target.Height!=transparent.Height)
            {
                RetireTarget();target=DynamicGpuFramebuffer.Wrap(transparent.FboId,"Cloud.Volume.Transparent",transparent.Width,transparent.Height);
                target.PublishRenderPassMetadata();
            }
            if(!view!.Draw(publication,input,scene,target,Current,CloudStoredVolumeDiagnostics.Mode)) throw new InvalidOperationException(view.Failure??"Cloud view rejected.");
            }
            // Optional diagnostics inspect completed working data before another update is submitted.
            try {CloudStoredVolumeDiagnostics.Observe(solution,input.Frame);}
            catch(Exception error) when(!EngineBoundaryRestoreException.IsRestorationFailure(error))
            {api.Logger.Warning("[VGE] Cloud lighting diagnostics unavailable: {0}",error.Message);}
            solution.Submit(input,map,lighting,VgeFrameRenderer.Current,settings,Current);
            if(solution.Failure is not null)throw new InvalidOperationException(solution.Failure);
        }
        catch(Exception error) when(!EngineBoundaryRestoreException.IsRestorationFailure(error)) {Fail(error);}
    }
    #endregion
    #region Lifetime
    /// <summary>Clears executables, persistent light and bounds together for explicit reload/recovery.</summary>
    public void Reset()
    {
        prepared=failed=false;admitted=null;
        RenderingCleanup.Run(()=>{view?.Dispose();view=null;},()=>{solution?.Dispose();solution=null;},RetireTarget);
    }
    /// <summary>Unregisters visible drawing before disposing every private owner and resize callback.</summary>
    public void Dispose()
    {
        if(disposed) return;disposed=true;
        RenderingCleanup.Run(()=>api.Event.UnregisterRenderer(this,EnumRenderStage.OIT),unregisterResize,Reset);
    }
    #endregion
    #endregion
    #region Private
    /// <summary>Retires framebuffer metadata on resize while cloud-space history remains compatible.</summary>
    private void RetireTarget() {target?.Dispose();target=null;view?.ReleaseTargets();}
    /// <summary>Latches admitted failure until recovery without releasing native suppression midway through the frame.</summary>
    private void Fail(Exception error)
    {
        failed=true;solution?.Storage.Withdraw();
        api.Logger.Error("[VGE] Stored-volume clouds disabled until reload/reset/mode change. {0}",error);
    }
    #endregion
}
