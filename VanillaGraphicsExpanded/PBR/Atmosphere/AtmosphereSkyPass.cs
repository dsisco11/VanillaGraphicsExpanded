using System;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.ModSystems;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Rendering.Pipeline;
using VanillaGraphicsExpanded.Rendering.Pipeline.Descriptions;
using VanillaGraphicsExpanded.Rendering.Pipeline.Passes;
using VanillaGraphicsExpanded.Rendering.Shaders;
using Vintagestory.API.Client;

namespace VanillaGraphicsExpanded.PBR.Atmosphere;

/// <summary>Owns procedural fullscreen geometry and drawing while consuming atmospheric publication.</summary>
internal sealed class AtmosphereSkyPass : IDisposable
{
    private System.IDisposable? reloadRegistration;
    private static AtmosphereSkyPass? active;
    private readonly ICoreClientAPI api;
    private readonly GBufferManager gbuffer;
    private readonly GraphicsPipelineLifetime lifetime = new();
    private readonly Action unregisterResize;
    private static readonly VertexLayoutDesc Layout = new([]);
    private ArrayGraphicsGeometry? geometry;
    private GraphicsPipeline? pipeline;
    private bool failed;
    private bool disposed;

    #region Public API
    /// <summary>Attaches draw ownership and retires owned geometry through existing screen/world events.</summary>
    internal AtmosphereSkyPass(ICoreClientAPI api, GBufferManager gbuffer)
    {
        this.api = api;
        this.gbuffer = gbuffer;
        unregisterResize = ScreenResourceManager.Register(ScreenResourceManager.CompositeOrder, Reset);
        api.Event.LeaveWorld += Reset;
        reloadRegistration = VanillaGraphicsExpanded.Rendering.Integration.EngineShaderReload.Register(api, Reload);
        active = this;
    }

    /// <summary>Reports replacement lifecycle ownership independently of per-frame resource readiness.</summary>
    internal static bool IsEnabled => active is not null;

    /// <summary>Prepares an independently recoverable draw for the owning sky renderer.</summary>
    internal SkyDraw? PrepareDraw()
    {
        if (disposed || !ReferenceEquals(active, this) || AtmosphereModSystem.Lighting is null || !PrepareFrame()) return null;
        try { return CreateDraw(); }
        catch (Exception error) when (!EngineBoundaryRestoreException.IsRestorationFailure(error))
        {
            ReportFailure(error);
            return null;
        }
    }

    /// <summary>Withdraws ownership before retiring pipeline and owned geometry.</summary>
    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        if (ReferenceEquals(active, this)) active = null;
        unregisterResize();
        api.Event.LeaveWorld -= Reset;
        reloadRegistration?.Dispose();
        Reset(); lifetime.Dispose();
    }
    /// <summary>Prepares owned drawing resources without changing sky-system ownership.</summary>
    internal bool PrepareFrame()
    {
        if (disposed || failed || api.Render.FrameWidth <= 0 || api.Render.FrameHeight <= 0
            || api.Render.WireframeDebugRender.Vertex) return false;
        try
        {
            if (GpuShaderProgramLibraries.Get<AtmosphereSkyShaderProgram>(api, "pbr_sky")?.EnsureReady() != true) return false;
            // A private empty VAO supplies the core-profile draw contract; vertices come from gl_VertexID.
            geometry ??= new ArrayGraphicsGeometry(Layout, PrimitiveType.Triangles,
                new System.Collections.Generic.Dictionary<int, GpuVbo>(), proceduralVertices: 3);
            var frames = api.Render.FrameBuffers;
            if (frames[(int)EnumFrameBuffer.Primary] is not { } primary
                || frames[(int)EnumFrameBuffer.LiquidDepth]?.DepthTextureId is not > 0) return false;
            return gbuffer.PrimaryFramebuffer.FboId == primary.FboId && gbuffer.PrimaryFramebuffer.HasRenderPassMetadata;
        }
        catch (Exception error) when (!EngineBoundaryRestoreException.IsRestorationFailure(error))
        {
            failed = true;
            api.Logger.Error("[VGE] Sky preparation failed; owned sky drawing is disabled until reload/reset. {0}", error);
            return false;
        }
    }
    #endregion

    #region Private
    /// <summary>Disables failed drawing through the normal reload/reset lifecycle.</summary>
    private void ReportFailure(Exception error)
    {
        failed = true;
        api.Logger.Error("[VGE] Owned sky unavailable until reload; vanilla sky remains suppressed. {0}", error);
    }

    /// <summary>Prepares the owned fullscreen draw without invoking or reactivating the engine sky callback.</summary>
    private SkyDraw? CreateDraw()
    {
        var render = api.Render;
        if (failed || geometry is null || AtmosphereModSystem.Lighting is not { } lighting
            || AtmosphereModSystem.SkyTextureId == 0 || render.CurrentRenderStage != EnumRenderStage.Opaque) return null;
        var primary = render.FrameBuffers[(int)EnumFrameBuffer.Primary];
        var liquid = render.FrameBuffers[(int)EnumFrameBuffer.LiquidDepth];
        if (primary is null || liquid?.DepthTextureId is not > 0
            || render.CurrentFrameBuffer?.FboId != primary.FboId) return null;
        var shader = GpuShaderProgramLibraries.Get<AtmosphereSkyShaderProgram>(api, "pbr_sky");
        if (shader?.EnsureReady() != true) return null;
        var target = gbuffer.PrimaryFramebuffer;
        if (target.FboId != primary.FboId || !target.HasRenderPassMetadata) return null;
        // Use the same convention as all other contributors to primary.
        shader.Capture(api, lighting, sceneLinear: true);
        // Sky owns color and glow only; framebuffer clearing establishes background validity.
        var pass = new RenderPassDesc(target, [new(0), new(1)]);
        using var metadata = new RenderPassTargets(pass);
        var blend = new ColorBlendDesc { Enabled = true,
            SourceRgb = Celestial.CelestialDrawPass.IsEnabled ? BlendingFactorSrc.One : BlendingFactorSrc.SrcAlpha, DestinationRgb = BlendingFactorDest.OneMinusSrcAlpha,
            SourceAlpha = BlendingFactorSrc.SrcAlpha, DestinationAlpha = BlendingFactorDest.OneMinusSrcAlpha };
        var description = new GraphicsPipelineDesc(shader.GraphicsIdentity!, Layout,
            metadata.Signature, DynamicPipelineState.Viewport,
            blending: [blend, blend]);
        if (pipeline is null || pipeline.Description != description || pipeline.ExecutableRevision != shader.ExecutableRevision)
        {
            var candidate = new GraphicsPipeline(lifetime, description, shader);
            pipeline?.Dispose(); pipeline = candidate;
        }
        return new SkyDraw("Atmosphere.Sky", [pipeline], commands =>
        {
            commands.BeginPass(pass);
            commands.SetPipeline(pipeline);
            commands.SetDynamicState(new() { Viewport = commands.PassViewport });
            commands.Draw(geometry, new(0, 3));
            commands.EndPass();
        }, ReportFailure);
    }

    /// <summary>Retires the pipeline and procedural geometry at publication and world boundaries.</summary>
    private void Reset()
    {
        // Resource retirement does not release replacement ownership or restart vanilla rendering.
        pipeline?.Dispose(); pipeline = null;
        geometry?.Dispose(); geometry = null; failed = false;
    }
    /// <summary>Allows a fresh executable attempt after shader reload.</summary>
    private bool Reload() { Reset(); return true; }
    #endregion
}
