using System;
using System.Collections.Generic;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.ModSystems;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Rendering.Pipeline;
using VanillaGraphicsExpanded.Rendering.Pipeline.Descriptions;
using VanillaGraphicsExpanded.Rendering.Pipeline.Passes;
using VanillaGraphicsExpanded.Rendering.Shaders;
using Vintagestory.API.Client;

namespace VanillaGraphicsExpanded.PBR.Atmosphere.Celestial;

/// <summary>Owns lunar HDR drawing before atmospheric local occlusion, clouds and scene geometry.</summary>
internal sealed class MoonDrawPass : IDisposable
{
    private static MoonDrawPass? active;
    private readonly ICoreClientAPI api;
    private readonly GBufferManager gbuffer;
    private readonly GraphicsPipelineLifetime lifetime=new();
    private readonly IDisposable reload;
    private readonly Action unregisterResize;
    private ArrayGraphicsGeometry? geometry;
    private GraphicsPipeline? pipeline;
    private AtmosphereLighting? publication;
    private MoonDisk disk;
    private (nint Handle,long Generation) context;
    private uint frameIndex;
    private float maximum;
    private bool admitted,failed,disposed;

    #region Public API
    /// <summary>Reports ownership independently of source readiness.</summary>
    internal static bool IsEnabled => active is { disposed:false };
    /// <summary>Provides the CPU-certified surface peak for frozen frame storage.</summary>
    internal static float MaximumNits => active?.maximum ?? 0;
    /// <summary>Attaches GPU resource ownership to resize, world and executable lifetimes.</summary>
    internal MoonDrawPass(ICoreClientAPI api,GBufferManager gbuffer)
    {
        this.api=api;this.gbuffer=gbuffer;
        unregisterResize=ScreenResourceManager.Register(ScreenResourceManager.CompositeOrder,Reset);
        reload=Rendering.Integration.EngineShaderReload.Register(api,Reload);
        api.Event.LeaveWorld+=Reset;
        active=this;
    }
    /// <summary>Admits coherent source and view inputs before scene storage is frozen.</summary>
    internal void PrepareFrame()
    {
        if (disposed || !ReferenceEquals(active, this)) return;
        try { Prepare(); }
        catch (Exception error) when (!EngineBoundaryRestoreException.IsRestorationFailure(error))
        {
            ReportFailure(error);
        }
    }
    /// <summary>Prepares an independently recoverable draw for the owning sky renderer.</summary>
    internal SkyDraw? PrepareDraw()
    {
        if (disposed || !ReferenceEquals(active, this) || !admitted) return null;
        try { return CreateDraw(); }
        catch (Exception error) when (!EngineBoundaryRestoreException.IsRestorationFailure(error))
        {
            ReportFailure(error);
            return null;
        }
    }
    /// <summary>Withdraws lifecycle handlers and retires owned graphics resources.</summary>
    public void Dispose()
    {
        if(disposed) return;disposed=true;
        if(ReferenceEquals(active,this)) active=null;
        api.Event.LeaveWorld-=Reset;unregisterResize();reload.Dispose();
        Rendering.Integration.RenderingCleanup.Run(Reset,lifetime.Dispose);
    }
    #endregion
    #region Private
    /// <summary>Disables a failed pass until its existing reset or reload recovery.</summary>
    private void ReportFailure(Exception error)
    {
        failed=true;admitted=false;maximum=0;
        api.Logger.Error("[VGE] Owned moon drawing failed until reload/reset; native celestial drawing remains disabled. {0}",error);
    }

    /// <summary>Caches disk integration by publication identity, while admitting camera and executable state each frame.</summary>
    private void Prepare()
    {
        var current=Rendering.Integration.RenderContextRegistry.Current();
        if(context!=default && context!=current) Reset();
        context=current;admitted=false;maximum=0;
        var lighting=AtmosphereModSystem.Lighting;
        if(current==default || failed || lighting is null || api.Render.WireframeDebugRender.Vertex) return;
        if(!ReferenceEquals(publication,lighting))
        {
            disk=MoonDisk.Capture(lighting);publication=lighting;
        }
        if(disk.Radiance==System.Numerics.Vector3.Zero) return;
        var program=GpuShaderProgramLibraries.Get<MoonProgram>(api,"celestial_moon");
        if(program?.EnsureReady()!=true) throw new InvalidOperationException("Owned lunar executable is unavailable.");
        geometry ??= new(CelestialDrawPass.FullscreenLayout,PrimitiveType.Triangles,
            new Dictionary<int,GpuVbo>(),proceduralVertices:3);
        program.DrawInputs.Capture(disk);
        maximum=disk.MaximumNits;frameIndex=VgeFrameRenderer.Current.FrameIndex;admitted=true;
    }
    /// <summary>Adds physical radiance using tracked blend state, preserving depth and the engine boundary.</summary>
    private SkyDraw? CreateDraw()
    {
        if(frameIndex!=VgeFrameRenderer.Current.FrameIndex || !ReferenceEquals(publication,AtmosphereModSystem.Lighting))
            throw new InvalidOperationException("Lunar publication changed after frame admission.");
        var primary=api.Render.FrameBuffers[(int)EnumFrameBuffer.Primary];
        var target=gbuffer.PrimaryFramebuffer;
        if(primary is null || target.FboId!=primary.FboId || api.Render.CurrentFrameBuffer?.FboId!=primary.FboId
            || !target.HasRenderPassMetadata) return null;
        var program=GpuShaderProgramLibraries.Get<MoonProgram>(api,"celestial_moon")!;
        var pass=new RenderPassDesc(target,[new(0),new(1)]);
        using var metadata=new RenderPassTargets(pass);
        var blend=new ColorBlendDesc {Enabled=true,SourceRgb=BlendingFactorSrc.One,DestinationRgb=BlendingFactorDest.One,
            SourceAlpha=BlendingFactorSrc.Zero,DestinationAlpha=BlendingFactorDest.One};
        var description=new GraphicsPipelineDesc(program.GraphicsIdentity!,CelestialDrawPass.FullscreenLayout,
            metadata.Signature,DynamicPipelineState.Viewport,blending:[blend,blend]);
        if(pipeline is null || pipeline.Description!=description || pipeline.ExecutableRevision!=program.ExecutableRevision)
        {
            var candidate=new GraphicsPipeline(lifetime,description,program);pipeline?.Dispose();pipeline=candidate;
        }
        return new SkyDraw("Celestial.Moon", [pipeline], commands=>
        {
            commands.BeginPass(pass);commands.SetPipeline(pipeline);
            commands.SetDynamicState(new(){Viewport=commands.PassViewport});
            commands.Draw(geometry!,new(0,3));commands.EndPass();
        }, ReportFailure);
    }
    /// <summary>Retires frame and executable resources without reactivating native callbacks.</summary>
    private void Reset()
    {
        admitted=false;failed=false;maximum=0;publication=null;
        Rendering.Integration.RenderingCleanup.Run(
            ()=>{pipeline?.Dispose();pipeline=null;},()=>{geometry?.Dispose();geometry=null;});
    }
    /// <summary>Retries source and executable admission after a shader reload.</summary>
    private bool Reload() {Reset();return true;}
    #endregion
}
