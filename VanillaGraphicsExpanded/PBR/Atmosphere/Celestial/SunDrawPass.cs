using VanillaGraphicsExpanded.Rendering.ShaderCompilation;
using VanillaGraphicsExpanded.Rendering.RHI;
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

/// <summary>Owns solar disk scheduling and drawing before local atmospheric coverage and scene geometry.</summary>
internal sealed class SunDrawPass : IDisposable
{
    private static SunDrawPass? active;
    private readonly ICoreClientAPI api;
    private readonly GBufferManager gbuffer;
    private readonly IDisposable reload;
    private readonly Action unregisterResize;
    private ArrayGraphicsGeometry? geometry;
    private GraphicsPipelineAsset? pipeline;
    private AtmosphereLighting? publication;
    private (nint Handle, long Generation) context;
    private uint frameIndex;
    private bool admitted, failed, disposed;

    #region Public API
    /// <summary>Reports renderer ownership independently of atmospheric readiness.</summary>
    internal static bool IsEnabled => active is { disposed: false };
    /// <summary>Attaches GPU resource ownership to screen, world and shader lifecycles.</summary>
    internal SunDrawPass(ICoreClientAPI api, GBufferManager gbuffer)
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
        if (disposed || !ReferenceEquals(active, this) || !admitted) return null;
        try { return CreateDraw(); }
        catch (Exception error) when (!EngineBoundaryRestoreException.IsRestorationFailure(error))
        {
            ReportFailure(error);
            return null;
        }
    }
    /// <summary>Withdraws lifecycle handlers before retiring the owned pipeline and procedural geometry.</summary>
    public void Dispose()
    {
        if (disposed) return; disposed = true;
        if (ReferenceEquals(active, this)) active = null;
        api.Event.LeaveWorld -= Reset; unregisterResize(); reload.Dispose();
        Rendering.Integration.RenderingCleanup.Run(Reset);
    }
    #endregion

    #region Private
    /// <summary>Disables a failed pass until its existing reset or reload recovery.</summary>
    private void ReportFailure(Exception error)
    {
        failed = true; admitted = false;
        api.Logger.Error("[VGE] Owned sun drawing failed until reload/reset; native celestial drawing remains disabled. {0}", error);
    }

    /// <summary>Admits finite solar radiance without touching native shader, query or mesh ownership.</summary>
    private void Prepare()
    {
        var current = Rendering.Integration.RenderContextRegistry.Current();
        if (context != default && context != current) Reset();
        context = current; admitted = false;
        var lighting = AtmosphereModSystem.Lighting;
        if (current == default || failed || lighting is null || api.Render.WireframeDebugRender.Vertex) return;
        Vector3 radiance = AtmosphereSolarDisk.Radiance(lighting);
        if (!float.IsFinite(radiance.X) || !float.IsFinite(radiance.Y) || !float.IsFinite(radiance.Z)
            || radiance.X < 0 || radiance.Y < 0 || radiance.Z < 0)
            throw new InvalidOperationException("Solar disk radiance is outside the finite physical domain.");
        if (radiance == Vector3.Zero) return;
        var program = GpuShaderProgramLibraries.Get<SunProgram>(api, "celestial_sun");
        if (program?.EnsureReady() != true) throw new InvalidOperationException("Owned solar executable is unavailable.");
        geometry ??= new(CelestialDrawPass.FullscreenLayout, PrimitiveTopology.Triangles,
            new Dictionary<int, GpuVbo>(), proceduralVertices: 3);
        publication = lighting; frameIndex = VgeFrameRenderer.Current.FrameIndex; admitted = true;
    }
    /// <summary>Adds the guarded HDR disk and solar metadata while preserving primary depth and caller state.</summary>
    private SkyDraw? CreateDraw()
    {
        if (frameIndex != VgeFrameRenderer.Current.FrameIndex || !ReferenceEquals(publication, AtmosphereModSystem.Lighting))
            throw new InvalidOperationException("Solar publication changed after frame admission.");
        var primary = api.Render.FrameBuffers[(int)EnumFrameBuffer.Primary];
        var target = gbuffer.PrimaryFramebuffer;
        if (primary is null || target.FboId != primary.FboId || api.Render.CurrentFrameBuffer?.FboId != primary.FboId
            || !target.HasRenderPassMetadata) return null;
        var program = GpuShaderProgramLibraries.Get<SunProgram>(api, "celestial_sun")!;
        // Glare settings are captured later in Before, but are complete before any Opaque draw.
        var glare = Postprocessing.PostprocessPipeline.GlareStrengths;
        float maximumScale = AtmosphereSolarDiskStorage.MaximumScale(AtmosphereSolarDisk.Radiance(publication!), glare.Bloom, glare.Shafts);
        if (maximumScale <= 0) throw new InvalidOperationException("Solar storage headroom could not be certified.");
        program.DrawInputs.Capture(publication!, maximumScale);
        var pass = new RHIRenderPassDescription(target.SurfaceReference,[new(AttachmentIndex:0),new(AttachmentIndex:1)]);
        var signature=RHICommandContext.Current.DescribeRenderTargets(pass);
        var blend = new ColorBlendDesc { Enabled = true, SourceRgb = BlendFactor.One, DestinationRgb = BlendFactor.One,
            SourceAlpha = BlendFactor.Zero, DestinationAlpha = BlendFactor.One };
        var description = new RHIGraphicsPipelineDescription(program.Shader, CelestialDrawPass.FullscreenLayout,
            signature, DynamicPipelineState.Viewport, blending: [blend, blend]);
        if (pipeline is null || pipeline.Description != description || pipeline.Description.Shader != program.Shader)
        {
            var candidate = new GraphicsPipelineAsset(program,description); pipeline?.Dispose(); pipeline = candidate;
        }
        return new SkyDraw("Celestial.Sun", [pipeline.Pipeline], commands =>
        {
            commands.BeginRenderPass(pass); commands.SetPipeline(pipeline.Pipeline);
            commands.SetDynamicState(new() { Viewport = commands.PassViewport });
            commands.BindResources(program.CaptureBindings());
            commands.Draw(geometry!.Inputs,new(0,3)); commands.EndRenderPass();
        }, ReportFailure);
    }
    /// <summary>Withdraws frame admission and retires resources without restoring native callbacks.</summary>
    private void Reset()
    {
        admitted = false; failed = false; publication = null;
        Rendering.Integration.RenderingCleanup.Run(
            () => { pipeline?.Dispose(); pipeline = null; }, () => { geometry?.Dispose(); geometry = null; });
    }
    /// <summary>Retries admission after shader reload without changing renderer registration.</summary>
    private bool Reload() { failed=false; return true; }
    #endregion
}
