using System;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.Rendering.Pipeline;
using VanillaGraphicsExpanded.Rendering.Pipeline.Descriptions;
using VanillaGraphicsExpanded.Rendering.Pipeline.Passes;
using VanillaGraphicsExpanded.Rendering.Shaders;
using Vintagestory.API.Client;
using Vintagestory.API.MathTools;
using VanillaGraphicsExpanded.Profiling;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Rendering.Profiling;

namespace VanillaGraphicsExpanded.PBR;

/// <summary>
/// Fullscreen direct lighting pass (Opaque stage).
/// Writes split radiance buffers into DirectLightingBufferManager:
/// - DirectDiffuse
/// - DirectSpecular
/// - Emissive
/// </summary>
public sealed partial class DirectLightingRenderer : IRenderer, IDisposable
{
    private const double RenderOrderValue = 9.0;
    private const int RenderRangeValue = 1;
    private static readonly RenderTargetSignature LightingTargets = new([
        new(PixelInternalFormat.Rgba16f), new(PixelInternalFormat.Rgba16f), new(PixelInternalFormat.Rgba16f)]);
    private static readonly RenderPassColor[] LightingOutputs = [
        new(0, AttachmentLoad.Clear, Clear: ColorClearValue.Float(0, 0, 0, 0)),
        new(1, AttachmentLoad.Clear, Clear: ColorClearValue.Float(0, 0, 0, 0)),
        new(2, AttachmentLoad.Clear, Clear: ColorClearValue.Float(0, 0, 0, 0))];

    private readonly ICoreClientAPI capi;
    private readonly GBufferManager gBufferManager;
    private readonly DirectLightingBufferManager bufferManager;

    private EngineFullscreenGeometry? geometry;
    private readonly GraphicsPipelineLifetime pipelineLifetime = new();
    private GraphicsPipeline? lightingPipeline;

    private readonly float[] invProjectionMatrix = new float[16];
    private readonly float[] invModelViewMatrix = new float[16];

    public double RenderOrder => RenderOrderValue;

    public int RenderRange => RenderRangeValue;

    #region Public API
    #region Rendering
    /// <summary>Registers direct lighting using the engine view-space light coordinates.</summary>
    public DirectLightingRenderer(
        ICoreClientAPI capi,
        GBufferManager gBufferManager,
        DirectLightingBufferManager bufferManager)
    {
        this.capi = capi;
        this.gBufferManager = gBufferManager;
        this.bufferManager = bufferManager;

        var shader = Rendering.Shaders.GpuShaderPrograms.Get<PBRDirectLightingShaderProgram>(capi, "pbr_direct_lighting");
        if (shader != null) Rendering.Shaders.GpuShaderPrograms.Preload(capi, [shader]);

        var quadMesh = QuadMeshUtil.GetCustomQuadModelData(-1, -1, 0, 2, 2);
        quadMesh.Rgba = null;
        geometry = EngineFullscreenGeometry.Upload(capi.Render, quadMesh);

        capi.Event.RegisterRenderer(this, EnumRenderStage.Opaque, "pbr_direct_lighting");

        capi.Logger.Notification("[VGE] DirectLightingRenderer registered (Opaque @ 9.0)");
    }

    /// <summary>Reconstructs terrain-relative receivers and renders direct lighting into its split targets.</summary>
    public void OnRenderFrame(float deltaTime, EnumRenderStage stage)
    {
        if (stage == EnumRenderStage.Opaque) RenderLighting();
    }

    /// <summary>Evaluates normal or isolated lighting targets under complete engine-state restoration.</summary>
    internal bool RenderLighting(DirectLightingTargets? isolated = null)
    {
        StateCache.Current.RequireOutsideEngineBoundary();
        if (geometry is null || capi.Render.FrameWidth <= 0 || capi.Render.FrameHeight <= 0) return false;
        var pipeline = PrepareBoundaryPipeline();
        if (pipeline is null) return false;
        bool rendered = false;
        // This ordinary opaque callback executes outside conditional rendering and transform feedback.
        if (!GraphicsCommandContext.TryRun("PBR.DirectLighting", [pipeline], true,
            commands => rendered = RenderLightingWithinBoundary(commands, pipeline, isolated)))
            throw new InvalidOperationException("Direct lighting engine boundary unavailable.");
        return rendered;
    }

    /// <summary>Refreshes the borrowed realization only after a successful executable publication.</summary>
    internal GraphicsPipeline? PrepareBoundaryPipeline()
    {
        StateCache.Current.RequireOutsideEngineBoundary();
        if (geometry is null) return null;
        var shader = GpuShaderPrograms.Get<PBRDirectLightingShaderProgram>(capi, "pbr_direct_lighting");
        if (shader?.EnsureReady() != true) return null;
        if (lightingPipeline is not null && ReferenceEquals(lightingPipeline.Shader, shader)
            && lightingPipeline.ExecutableRevision == shader.ExecutableRevision) return lightingPipeline;
        // Keep the previous realization until its replacement validates; native names are not identity.
        var replacement = new GraphicsPipeline(pipelineLifetime,
            new(shader.GraphicsIdentity!, EngineFullscreenGeometry.Layout, LightingTargets,
                DynamicPipelineState.Viewport, label: "PBR.DirectLighting"), shader);
        lightingPipeline?.Dispose();
        lightingPipeline = replacement;
        return replacement;
    }

    /// <summary>Draws shared lighting work without nesting an engine restoration boundary.</summary>
    internal bool RenderLightingWithinBoundary(GraphicsCommandContext commands, GraphicsPipeline pipeline,
        DirectLightingTargets? isolated = null)
    {
        if (geometry is null)
        {
            return false;
        }

        int screenW = capi.Render.FrameWidth;
        int screenH = capi.Render.FrameHeight;
        if (screenW <= 0 || screenH <= 0)
        {
            return false;
        }
        var primaryFb = capi.Render.FrameBuffers[(int)EnumFrameBuffer.Primary];
        if (primaryFb is null)
        {
            return false;
        }

        // Ensure output buffers match current screen size
        if (isolated is null ? !bufferManager.EnsureBuffers(screenW, screenH) : !isolated.IsValid)
        {
            return false;
        }

        // Compute inverse matrices
        MatrixHelper.Invert(capi.Render.CurrentProjectionMatrix, invProjectionMatrix);
        MatrixHelper.Invert(capi.Render.CameraMatrixOriginf, invModelViewMatrix);

        // Shader program
        var shader = (PBRDirectLightingShaderProgram)pipeline.Shader;
        if (shader.RequiresPreparation || shader.IsRetired)
        {
            return false;
        }

        // Target routing, clear masks and complete drawing state belong to submission owners.
        var target = isolated?.Framebuffer ?? bufferManager.DirectLightingFbo!;
        AssignInputs(shader, primaryFb);

        using var cpuScope = Profiler.BeginScope("PBR.DirectLighting", "Render");
        using (GlGpuProfiler.Instance.Scope("PBR.DirectLighting"))
        {
            commands.BeginPass(new(target!, LightingOutputs));
            commands.SetPipeline(pipeline);
            commands.SetDynamicState(new() { Viewport = commands.PassViewport });
            commands.Draw(geometry, new(0, 6));
            commands.EndPass();
        }

        return true;
    }

    #endregion

    #region Lifetime
    /// <summary>Releases the fullscreen mesh and unregisters the lighting callback.</summary>
    public void Dispose()
    {
        lightingPipeline?.Dispose();
        lightingPipeline = null;
        pipelineLifetime.Dispose();
        geometry?.Dispose();
        geometry = null;

        capi.Event.UnregisterRenderer(this, EnumRenderStage.Opaque);
    }
    #endregion
    #endregion
}
