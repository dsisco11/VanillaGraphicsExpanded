using System;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.ModSystems;
using VanillaGraphicsExpanded.PBR.SceneColor;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Rendering.Pipeline;
using VanillaGraphicsExpanded.Rendering.Pipeline.Descriptions;
using VanillaGraphicsExpanded.Rendering.Pipeline.Passes;
using VanillaGraphicsExpanded.Rendering.Shaders;
using Vintagestory.API.Client;

namespace VanillaGraphicsExpanded.PBR.CameraExposure;

/// <summary>Meters the completed HDR scene before generated glare and publishes GPU-resident camera exposure.</summary>
internal sealed class CameraExposureRenderer : IRenderer
{
    private static CameraExposureRenderer? active;
    private static readonly VertexLayoutDesc Layout = new([]);
    private readonly ICoreClientAPI api;
    private readonly GraphicsPipelineLifetime lifetime = new();
    private readonly CameraExposureHistory history = new();
    private readonly Action unregisterResize;
    private CameraExposureTargets? targets;
    private BorrowedTexture? sceneTexture;
    private ArrayGraphicsGeometry? geometry;
    private GraphicsPipeline? histogramPipeline, adaptationPipeline;
    private CameraExposureParameters settings;
    private float deltaTime;
    private bool reset = true;
    private bool published, captured, disposed;

    #region Public API
    #region Lifetime
    /// <summary>Registers frame timing and resource lifecycle independently of the display shader.</summary>
    internal CameraExposureRenderer(ICoreClientAPI api)
    {
        this.api = api;
        unregisterResize = ScreenResourceManager.Register(ScreenResourceManager.CompositeOrder, Retire);
        api.Event.LeaveWorld += Retire;
        api.Event.ReloadShader += Reload;
        api.Event.RegisterRenderer(this, EnumRenderStage.Before, "vge_camera_exposure");
        active = this;
    }
    /// <summary>Captures timing before scene rendering begins.</summary>
    public double RenderOrder => 1001;
    /// <summary>Does not depend on terrain range.</summary>
    public int RenderRange => int.MaxValue;
    /// <summary>Withdraws publication before unregistering and retiring GPU resources.</summary>
    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        if (ReferenceEquals(active, this)) active = null;
        api.Event.UnregisterRenderer(this, EnumRenderStage.Before);
        api.Event.LeaveWorld -= Retire;
        api.Event.ReloadShader -= Reload;
        unregisterResize(); Retire(); lifetime.Dispose();
    }
    #endregion

    #region Frame publication
    /// <summary>Snapshots settings and camera continuity once per frame without submitting GPU work.</summary>
    public void OnRenderFrame(float dt, EnumRenderStage stage)
    {
        if (disposed || stage != EnumRenderStage.Before) return;
        published = false;
        settings = ConfigModSystem.Config.CameraExposure.Snapshot();
        var position = api.World.Player.Entity.Pos;
        reset |= history.Capture(settings, dt, position.X, position.Y, position.Z,
            position.Yaw, position.Pitch, position.Dimension, (int)api.Render.CameraType);
        deltaTime = float.IsFinite(dt) ? Math.Clamp(dt, 0, 1) : 0;
        captured = true;
    }
    /// <summary>Runs at the engine postprocessing boundary after late scene contributors and before bloom/light shafts.</summary>
    internal static void MeterScene()
    {
        if (!SceneColorPipeline.HasSceneInput) return;
        if (active is not { } renderer)
            throw new InvalidOperationException("VGE HDR camera exposure owner is unavailable.");
        renderer.Meter();
    }
    /// <summary>Returns one coherent publication; automatic mode never silently substitutes manual exposure.</summary>
    internal static (GpuTexture? Texture, float ManualEV) DisplayExposure()
    {
        if (active is not { } renderer)
            throw new InvalidOperationException("VGE HDR camera exposure owner is unavailable.");
        if (!renderer.published) throw new InvalidOperationException("Camera exposure was not published for this HDR frame.");
        return (renderer.settings.Enabled ? renderer.targets!.Exposure : null,
            Math.Clamp(renderer.settings.ManualEV + renderer.settings.Compensation, renderer.settings.MinEV, renderer.settings.MaxEV));
    }
    #endregion
    #endregion

    #region Private
    #region Submission
    /// <summary>Submits fixed-size metering and temporal reduction, then atomically publishes the completed result.</summary>
    private void Meter()
    {
        if (published) return;
        if (!captured) throw new InvalidOperationException("Camera exposure frame inputs were not captured.");
        if (!settings.Enabled)
        {
            // Manual mode has no metering draws or retained histogram/history allocation.
            DisposeResources(); reset = true; published = true; return;
        }
        var histogram = GpuShaderPrograms.Get<CameraHistogramShaderProgram>(api, "pbr_camera_histogram");
        var adaptation = GpuShaderPrograms.Get<CameraAdaptShaderProgram>(api, "pbr_camera_adapt");
        if (histogram?.EnsureReady() != true || adaptation?.EnsureReady() != true)
            throw new InvalidOperationException("VGE camera exposure shaders are unavailable.");
        targets ??= new CameraExposureTargets();
        geometry ??= new ArrayGraphicsGeometry(Layout, PrimitiveType.Triangles,
            new System.Collections.Generic.Dictionary<int, GpuVbo>(), proceduralVertices: 3);
        var primary = api.Render.FrameBuffers[(int)EnumFrameBuffer.Primary];
        if (primary?.ColorTextureIds is not { Length: > 0 } colors || colors[0] == 0)
            throw new InvalidOperationException("VGE camera metering requires the completed HDR primary scene.");
        if (sceneTexture?.TextureId != colors[0]) { sceneTexture?.Dispose(); sceneTexture = new(colors[0]); }
        histogram.Capture(settings, deltaTime, reset); histogram.SceneRadiance = sceneTexture;
        adaptation.Capture(settings, deltaTime, reset);
        adaptation.Histogram = targets.Histogram; adaptation.PreviousExposure = targets.Exposure;
        PreparePipeline(ref histogramPipeline, histogram, targets.HistogramTarget);
        PreparePipeline(ref adaptationPipeline, adaptation, targets.WriteTarget);
        if (!GraphicsCommandContext.TryRun("Camera.Exposure", [histogramPipeline!, adaptationPipeline!], true, commands =>
        {
            Submit(commands, histogramPipeline!, targets.HistogramTarget);
            Submit(commands, adaptationPipeline!, targets.WriteTarget);
        })) throw new InvalidOperationException("VGE camera exposure graphics boundary was rejected.");
        targets.Publish(); reset = false; published = true;
    }
    /// <summary>Refreshes PSOs only when their executable or target contract changes.</summary>
    private void PreparePipeline(ref GraphicsPipeline? pipeline, GpuProgram shader, GpuFramebuffer target)
    {
        using var metadata = new RenderPassTargets(new RenderPassDesc(target, [new(0)]));
        var description = new GraphicsPipelineDesc(shader.GraphicsIdentity!, Layout, metadata.Signature, DynamicPipelineState.Viewport);
        if (pipeline is not null && pipeline.Description == description && pipeline.ExecutableRevision == shader.ExecutableRevision) return;
        var candidate = new GraphicsPipeline(lifetime, description, shader);
        pipeline?.Dispose(); pipeline = candidate;
    }
    /// <summary>Overwrites one complete reduction target using the pass viewport and declarative graphics state.</summary>
    private void Submit(GraphicsCommandContext commands, GraphicsPipeline pipeline, GpuFramebuffer target)
    {
        commands.BeginPass(new RenderPassDesc(target, [new(0)]));
        commands.SetPipeline(pipeline);
        commands.SetDynamicState(new() { Viewport = commands.PassViewport });
        commands.Draw(geometry!, new(0, 3));
        commands.EndPass();
    }
    #endregion

    #region Retirement
    /// <summary>Releases pipelines before their borrowed targets and owned procedural geometry.</summary>
    private void DisposeResources()
    {
        histogramPipeline?.Dispose(); histogramPipeline = null;
        adaptationPipeline?.Dispose(); adaptationPipeline = null;
        targets?.Dispose(); targets = null;
        sceneTexture?.Dispose(); sceneTexture = null;
        geometry?.Dispose(); geometry = null;
    }
    /// <summary>Invalidates history and frame publication on world, storage and shader lifecycle changes.</summary>
    private void Retire()
    {
        published = captured = false; reset = true; history.Reset(); DisposeResources();
    }
    /// <summary>Retires executable-dependent resources before accepting a shader reload.</summary>
    private bool Reload() { Retire(); return true; }
    #endregion
    #endregion
}
