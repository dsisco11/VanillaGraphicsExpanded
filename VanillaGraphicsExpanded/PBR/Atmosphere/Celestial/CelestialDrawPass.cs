using System;
using System.Collections.Generic;
using System.Numerics;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.ModSystems;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Rendering.Pipeline;
using VanillaGraphicsExpanded.Rendering.Pipeline.Descriptions;
using VanillaGraphicsExpanded.Rendering.Pipeline.Passes;
using VanillaGraphicsExpanded.Rendering.Shaders;
using Vintagestory.API.Client;

namespace VanillaGraphicsExpanded.PBR.Atmosphere.Celestial;

/// <summary>Owns galaxy and instanced stars before the atmosphere, independent of native night-sky readiness.</summary>
internal sealed class CelestialDrawPass : IDisposable
{
    private static CelestialDrawPass? active;
    private readonly ICoreClientAPI api;
    private readonly GBufferManager gbuffer;
    private readonly GraphicsPipelineLifetime lifetime = new();
    private readonly IDisposable reload;
    private readonly Action unregisterResize;
    private ArrayGraphicsGeometry? fullscreen;
    private (nint Handle, long Generation) context;
    private GraphicsPipeline? galaxyPipeline, starPipeline;
    private CelestialResourceSet? source;
    private AtmosphereLighting? atmosphere;
    private bool failed, disposed;
    private float maximum;
    private uint frameIndex;
    internal static readonly VertexLayoutDesc FullscreenLayout = new([]);

    #region Public API
    /// <summary>Reports the owned draw lifecycle independently of resource readiness.</summary>
    internal static bool IsEnabled => active is { disposed: false };
    /// <summary>Returns the admitted all-overlap source peak for the frozen frame storage bound.</summary>
    internal static float MaximumNits => active?.maximum ?? 0;
    /// <summary>Attaches source and drawing resources without borrowing native star geometry or images.</summary>
    internal CelestialDrawPass(ICoreClientAPI api, GBufferManager gbuffer)
    {
        this.api = api; this.gbuffer = gbuffer;
        unregisterResize = ScreenResourceManager.Register(ScreenResourceManager.CompositeOrder, Reset);
        reload = Rendering.Integration.EngineShaderReload.Register(api, Reload);
        api.Event.LeaveWorld += Reset;
        active = this;
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
        if (disposed || !ReferenceEquals(active, this) || failed || source is null) return null;
        try { return CreateDraw(); }
        catch (Exception error) when (!EngineBoundaryRestoreException.IsRestorationFailure(error))
        {
            ReportFailure(error);
            return null;
        }
    }
    /// <summary>Releases ownership and every independently owned resource on teardown.</summary>
    public void Dispose()
    {
        if (disposed) return; disposed = true;
        if (ReferenceEquals(active,this)) active = null;
        api.Event.LeaveWorld -= Reset; unregisterResize(); reload.Dispose();
        Rendering.Integration.RenderingCleanup.Run(Reset,lifetime.Dispose);
    }
    #endregion
    #region Private
    #region Preparation and submission

    /// <summary>Stages immutable camera/source inputs; no catalog rebuilding or instance uploads occur here.</summary>
    private void Prepare()
    {
        var current = Rendering.Integration.RenderContextRegistry.Current();
        if (context != default && context != current) Reset();
        context = current;
        source = null; maximum = 0;
        if (current == default || failed || api.Render.WireframeDebugRender.Vertex) return;
        var completed = api.ModLoader.GetModSystem<AtmosphereModSystem>().CelestialSource;
        var lighting = AtmosphereModSystem.Lighting;
        if (completed is null || lighting is null || !completed.Galaxy.IsValid
            || !ReferenceEquals(completed.Source, lighting.CelestialSource)) return;
        var stars = GpuShaderProgramLibraries.Get<CelestialStarProgram>(api,"celestial_stars");
        var galaxy = GpuShaderProgramLibraries.Get<CelestialGalaxyProgram>(api,"celestial_galaxy");
        if (stars?.EnsureReady() != true || galaxy?.EnsureReady() != true)
            throw new InvalidOperationException("Owned celestial executables are unavailable.");
        fullscreen ??= new(FullscreenLayout,PrimitiveType.Triangles,new Dictionary<int,GpuVbo>(),proceduralVertices:3);
        float bound = CelestialDrawAdmission.MaximumNits(completed.Source,VgeFrameRenderer.Current);
        var transmission = AtmosphereModSystem.ExternalTransmissionTexture;
        if (transmission is not { IsValid: true }) return;
        Matrix4x4 rotation = lighting.CelestialRotation;
        stars.DrawInputs.Capture(rotation,1/completed.Source.StorageScale,lighting.HorizonElevation);
        galaxy.DrawInputs.Capture(rotation,1/completed.Source.StorageScale,lighting.HorizonElevation);
        stars.GalaxyMap = completed.Galaxy; galaxy.GalaxyMap = completed.Galaxy;
        stars.ExternalTransmission = transmission; galaxy.ExternalTransmission = transmission;
        source = completed; atmosphere = lighting; maximum = bound; frameIndex = VgeFrameRenderer.Current.FrameIndex;
    }
    /// <summary>Draws both contributors into primary linear HDR with tracked additive state and no depth writes.</summary>
    private SkyDraw? CreateDraw()
    {
        if (frameIndex != VgeFrameRenderer.Current.FrameIndex || !ReferenceEquals(atmosphere,AtmosphereModSystem.Lighting))
            throw new InvalidOperationException("Celestial draw inputs changed after frame admission.");
        var primary = api.Render.FrameBuffers[(int)EnumFrameBuffer.Primary];
        var target = gbuffer.PrimaryFramebuffer;
        if (primary is null || target.FboId != primary.FboId || api.Render.CurrentFrameBuffer?.FboId != primary.FboId
            || !target.HasRenderPassMetadata) return null;
        var stars = GpuShaderProgramLibraries.Get<CelestialStarProgram>(api,"celestial_stars")!;
        var galaxy = GpuShaderProgramLibraries.Get<CelestialGalaxyProgram>(api,"celestial_galaxy")!;
        var pass = new RenderPassDesc(target,[new(0),new(1)]);
        using var metadata = new RenderPassTargets(pass);
        var blend = new ColorBlendDesc { Enabled = true, SourceRgb = BlendingFactorSrc.One,
            DestinationRgb = BlendingFactorDest.One, SourceAlpha = BlendingFactorSrc.Zero, DestinationAlpha = BlendingFactorDest.One };
        galaxyPipeline = PreparePipeline(galaxyPipeline,galaxy,FullscreenLayout,metadata.Signature,blend);
        starPipeline = PreparePipeline(starPipeline,stars,CelestialStarGeometry.Layout,metadata.Signature,blend);
        return new SkyDraw("Celestial.Sky", [galaxyPipeline,starPipeline], commands =>
        {
            commands.BeginPass(pass);
            commands.SetPipeline(galaxyPipeline);
            commands.SetDynamicState(new() { Viewport = commands.PassViewport });
            commands.Draw(fullscreen!,new(0,3));
            commands.SetPipeline(starPipeline); commands.Draw(source!.Stars.Geometry,new(0,6,InstanceCount:source.Stars.Count));
            commands.EndPass();
        }, ReportFailure);
    }
    /// <summary>Reuses the existing pipeline until its executable or target signature changes.</summary>
    private GraphicsPipeline PreparePipeline(GraphicsPipeline? old, GpuProgram program, VertexLayoutDesc layout,
        RenderTargetSignature signature, ColorBlendDesc blend)
    {
        var description = new GraphicsPipelineDesc(program.GraphicsIdentity!,layout,signature,DynamicPipelineState.Viewport,blending:[blend,blend]);
        if (old is not null && old.Description == description && old.ExecutableRevision == program.ExecutableRevision) return old;
        var candidate = new GraphicsPipeline(lifetime,description,program); old?.Dispose(); return candidate;
    }
    #endregion

    #region Recovery
    /// <summary>Disables a failed pass until its existing reset or reload recovery.</summary>
    private void ReportFailure(Exception error)
    {
        failed = true; source = null; maximum = 0;
        api.Logger.Error("[VGE] Owned celestial drawing failed until reload/reset; native night-sky drawing remains disabled. {0}",error);
    }

    /// <summary>Withdraws frame admission and pipelines without reactivating native drawing.</summary>
    private void Reset()
    {
        source = null; atmosphere = null; maximum = 0; failed = false;
        Rendering.Integration.RenderingCleanup.Run(
            () => { galaxyPipeline?.Dispose(); galaxyPipeline = null; },
            () => { starPipeline?.Dispose(); starPipeline = null; },
            () => { fullscreen?.Dispose(); fullscreen = null; });
    }
    /// <summary>Retries executable preparation while retaining independently completed source resources.</summary>
    private bool Reload() { Reset(); return true; }
    #endregion
    #endregion
}
