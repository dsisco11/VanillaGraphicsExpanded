using VanillaGraphicsExpanded.Rendering.ShaderCompilation;
using VanillaGraphicsExpanded.Rendering.RHI;
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
        new(TextureFormat.Rgba16f), new(TextureFormat.Rgba16f), new(TextureFormat.Rgba16f)]);

    private readonly ICoreClientAPI capi;
    private readonly IDisposable? reloadPreload;
    private readonly GBufferManager gBufferManager;
    private readonly DirectLightingBufferManager bufferManager;

    private EngineFullscreenGeometry? geometry;
    private GraphicsPipelineAsset? lightingPipeline;


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

        var shader = Rendering.Shaders.GpuShaderProgramLibraries.Get<PBRDirectLightingShaderProgram>(capi, "pbr_direct_lighting");
        if (shader != null) Rendering.Shaders.GpuShaderProgramLibraries.Preload(capi, [shader]);
        reloadPreload = Rendering.Integration.ClientRenderingLifetime.Find(capi)?.RegisterPreload(() =>
        {
            if (shader != null && !Rendering.Shaders.GpuShaderProgramLibraries.Preload(capi, [shader]))
                throw new InvalidOperationException("VGE direct lighting preparation failed after native reload.", shader.PreparationFailure);
        });

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
        var commands = RHICommandContext.Current;
        using var boundary = commands.EnterBoundary(new("PBR.DirectLighting", true, graphics: [pipeline], operations: BoundaryOperations.RenderPasses));
        boundary.Run(() => rendered = RenderLightingWithinBoundary(commands, pipeline, isolated));
        return rendered;
    }

    /// <summary>Refreshes the borrowed realization only after a successful executable publication.</summary>
    internal RHIGraphicsPipeline? PrepareBoundaryPipeline()
    {
        StateCache.Current.RequireOutsideEngineBoundary();
        if (geometry is null) return null;
        var shader = GpuShaderProgramLibraries.Get<PBRDirectLightingShaderProgram>(capi, "pbr_direct_lighting");
        if (shader?.EnsureReady() != true) return null;
        if (lightingPipeline is not null && lightingPipeline.Description.Shader == shader.Shader) return lightingPipeline.Pipeline;
        // Keep the previous realization until its replacement validates; native names are not identity.
        var replacement = new GraphicsPipelineAsset(shader,new(shader.Shader, EngineFullscreenGeometry.Layout, LightingTargets,
                DynamicPipelineState.Viewport, label: "PBR.DirectLighting"));
        lightingPipeline?.Dispose();
        lightingPipeline = replacement;
        return replacement.Pipeline;
    }

    /// <summary>Draws shared lighting work without nesting an engine restoration boundary.</summary>
    internal bool RenderLightingWithinBoundary(RHICommandContext commands, RHIGraphicsPipeline pipeline,
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

        // Shader program
        var shader = GpuShaderProgramLibraries.Get<PBRDirectLightingShaderProgram>(capi, "pbr_direct_lighting");
        if (shader is null || shader.Shader != pipeline.Description.Shader || shader.RequiresPreparation || shader.IsRetired)
        {
            return false;
        }

        // Target routing, clear masks and complete drawing state belong to submission owners.
        var pass = isolated?.Pass ?? bufferManager.LightingPass!;
        AssignInputs(shader, primaryFb);

        using var cpuScope = Profiler.BeginScope("PBR.DirectLighting", "Render");
        using (GlGpuProfiler.Instance.Scope("PBR.DirectLighting"))
        {
            commands.BeginRenderPass(pass);
            commands.SetPipeline(pipeline);
            commands.SetDynamicState(new() { Viewport = commands.PassViewport });
            commands.BindResources(shader.CaptureBindings());
            commands.DrawIndexed(geometry.Inputs, new(0, 6));
            commands.EndRenderPass();
        }

        return true;
    }

    #endregion

    #region Lifetime
    /// <summary>Releases the fullscreen mesh and unregisters the lighting callback.</summary>
    public void Dispose()
    {
        reloadPreload?.Dispose();
        shadowNearInput?.Dispose(); shadowNearInput = null;
        shadowFarInput?.Dispose(); shadowFarInput = null;
        lightingPipeline?.Dispose();
        lightingPipeline = null;
        
        geometry?.Dispose();
        geometry = null;

        capi.Event.UnregisterRenderer(this, EnumRenderStage.Opaque);
    }
    #endregion
    #endregion
}
