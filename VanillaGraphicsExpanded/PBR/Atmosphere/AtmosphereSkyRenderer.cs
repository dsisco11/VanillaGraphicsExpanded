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
internal sealed class AtmosphereSkyRenderer : IRenderer
{
    private static AtmosphereSkyRenderer? active;
    private readonly ICoreClientAPI api;
    private readonly GBufferManager gbuffer;
    private readonly GraphicsPipelineLifetime lifetime = new();
    private readonly Action unregisterResize;
    private static readonly VertexLayoutDesc Layout = new([]);
    private ArrayGraphicsGeometry? geometry;
    private GraphicsPipeline? pipeline;
    private bool failed;
    /// <summary>Draws at the original opaque sky position after the night sky.</summary>
    public double RenderOrder => .2;
    /// <summary>The sky is camera relative and does not use terrain range culling.</summary>
    public int RenderRange => 1;

    #region Public API
    /// <summary>Attaches draw ownership and retires owned geometry through existing screen/world events.</summary>
    internal AtmosphereSkyRenderer(ICoreClientAPI api, GBufferManager gbuffer)
    {
        this.api = api;
        this.gbuffer = gbuffer;
        unregisterResize = ScreenResourceManager.Register(ScreenResourceManager.CompositeOrder, Reset);
        api.Event.LeaveWorld += Reset;
        api.Event.ReloadShader += Reload;
        active = this;
        api.Event.RegisterRenderer(this, EnumRenderStage.Opaque, "vge_sky");
    }

    /// <summary>Reports replacement lifecycle ownership independently of per-frame resource readiness.</summary>
    internal static bool IsEnabled => active is not null;

    /// <summary>Draws when its own resources are ready; environmental publication is independent.</summary>
    public void OnRenderFrame(float deltaTime, EnumRenderStage stage)
    {
        if (stage == EnumRenderStage.Opaque && ReferenceEquals(active, this)
            && AtmosphereModSystem.Lighting is not null && Prepare())
        {
            Draw();
        }
    }

    /// <summary>Withdraws ownership before retiring pipeline and owned geometry.</summary>
    public void Dispose()
    {
        if (ReferenceEquals(active, this)) active = null;
        api.Event.UnregisterRenderer(this, EnumRenderStage.Opaque);
        unregisterResize();
        api.Event.LeaveWorld -= Reset;
        api.Event.ReloadShader -= Reload;
        Reset(); lifetime.Dispose();
    }
    #endregion

    #region Private
    /// <summary>Prepares owned drawing resources without changing sky-system ownership.</summary>
    private bool Prepare()
    {
        if (failed || api.Render.FrameWidth <= 0 || api.Render.FrameHeight <= 0
            || api.Render.WireframeDebugRender.Vertex) return false;
        try
        {
            if (GpuShaderPrograms.Get<AtmosphereSkyShaderProgram>(api, "pbr_sky")?.EnsureReady() != true) return false;
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
    /// <summary>Submits the owned fullscreen triangle without invoking or reactivating the engine sky callback.</summary>
    private void Draw()
    {
        var render = api.Render;
        if (failed || geometry is null || AtmosphereModSystem.Lighting is not { } lighting
            || AtmosphereModSystem.SkyTextureId == 0 || render.CurrentRenderStage != EnumRenderStage.Opaque) return;
        var primary = render.FrameBuffers[(int)EnumFrameBuffer.Primary];
        var liquid = render.FrameBuffers[(int)EnumFrameBuffer.LiquidDepth];
        if (primary is null || liquid?.DepthTextureId is not > 0
            || render.CurrentFrameBuffer?.FboId != primary.FboId) return;
        try
        {
            var shader = GpuShaderPrograms.Get<AtmosphereSkyShaderProgram>(api, "pbr_sky");
            if (shader?.EnsureReady() != true) return;
            var target = gbuffer.PrimaryFramebuffer;
            if (target.FboId != primary.FboId || !target.HasRenderPassMetadata) return;
            // The shared HDR owner has not activated a scene convention yet. The owned
            // shader retains both branches, while this runtime boundary stays compatible.
            shader.Capture(api, lighting, sceneLinear: false);
            // Sky owns color and glow only; framebuffer clearing establishes background validity.
            var pass = new RenderPassDesc(target, [new(0), new(1)]);
            using var metadata = new RenderPassTargets(pass);
            var blend = new ColorBlendDesc { Enabled = true,
                SourceRgb = BlendingFactorSrc.SrcAlpha, DestinationRgb = BlendingFactorDest.OneMinusSrcAlpha,
                SourceAlpha = BlendingFactorSrc.SrcAlpha, DestinationAlpha = BlendingFactorDest.OneMinusSrcAlpha };
            var description = new GraphicsPipelineDesc(shader.GraphicsIdentity!, Layout,
                metadata.Signature, DynamicPipelineState.Viewport,
                blending: [blend, blend]);
            if (pipeline is null || pipeline.Description != description || pipeline.ExecutableRevision != shader.ExecutableRevision)
            {
                var candidate = new GraphicsPipeline(lifetime, description, shader);
                pipeline?.Dispose(); pipeline = candidate;
            }
            if (!GraphicsCommandContext.TryRun("Atmosphere.Sky", [pipeline], true, commands =>
            {
                commands.BeginPass(pass);
                commands.SetPipeline(pipeline);
                commands.SetDynamicState(new() { Viewport = commands.PassViewport });
                commands.Draw(geometry, new(0, 3));
                commands.EndPass();
            }))
            {
                failed = true;
                api.Logger.Error("[VGE] Owned sky graphics boundary was rejected; drawing is disabled until reload/reset.");
            }
        }
        catch (Exception error) when (!EngineBoundaryRestoreException.IsRestorationFailure(error))
        {
            failed = true;
            api.Logger.Error("[VGE] Owned sky unavailable until reload; vanilla sky remains suppressed. {0}", error.ToString());
            return;
        }
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
