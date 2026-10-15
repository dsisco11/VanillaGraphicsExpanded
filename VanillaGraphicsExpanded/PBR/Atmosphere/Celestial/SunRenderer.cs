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
internal sealed class SunRenderer : IRenderer
{
    private static SunRenderer? active;
    private readonly ICoreClientAPI api;
    private readonly GBufferManager gbuffer;
    private readonly GraphicsPipelineLifetime lifetime = new();
    private readonly IDisposable reload;
    private readonly Action unregisterResize;
    private ArrayGraphicsGeometry? geometry;
    private GraphicsPipeline? pipeline;
    private AtmosphereLighting? publication;
    private (nint Handle, long Generation) context;
    private uint frameIndex;
    private bool admitted, failed, disposed;

    #region Public API
    /// <summary>Draws after the moon and before atmospheric fog/underwater coverage at order 0.2.</summary>
    public double RenderOrder => .197;
    /// <summary>The directional emitter is independent of terrain render distance.</summary>
    public int RenderRange => 1;
    /// <summary>Reports renderer ownership independently of atmospheric readiness.</summary>
    internal static bool IsEnabled => active is { disposed: false };
    /// <summary>Registers preparation and drawing with the existing screen, world and shader lifecycles.</summary>
    internal SunRenderer(ICoreClientAPI api, GBufferManager gbuffer)
    {
        this.api = api; this.gbuffer = gbuffer;
        unregisterResize = ScreenResourceManager.Register(ScreenResourceManager.CompositeOrder, Reset);
        reload = Rendering.Integration.EngineShaderReload.Register(api, Reload);
        api.Event.LeaveWorld += Reset;
        api.Event.RegisterRenderer(this, EnumRenderStage.Before, "vge_sun_prepare");
        api.Event.RegisterRenderer(this, EnumRenderStage.Opaque, "vge_sun");
        active = this;
    }
    /// <summary>Admits one completed atmosphere publication and draws only that publication in its matching frame.</summary>
    public void OnRenderFrame(float deltaTime, EnumRenderStage stage)
    {
        if (disposed || !ReferenceEquals(active, this)) return;
        try
        {
            if (stage == EnumRenderStage.Before) Prepare();
            else if (stage == EnumRenderStage.Opaque && admitted) Draw();
        }
        catch (Exception error) when (!EngineBoundaryRestoreException.IsRestorationFailure(error))
        {
            failed = true; admitted = false;
            api.Logger.Error("[VGE] Owned sun drawing failed until reload/reset; native celestial drawing remains disabled. {0}", error);
        }
    }
    /// <summary>Withdraws registrations before retiring the owned pipeline and procedural geometry.</summary>
    public void Dispose()
    {
        if (disposed) return; disposed = true;
        if (ReferenceEquals(active, this)) active = null;
        api.Event.UnregisterRenderer(this, EnumRenderStage.Before);
        api.Event.UnregisterRenderer(this, EnumRenderStage.Opaque);
        api.Event.LeaveWorld -= Reset; unregisterResize(); reload.Dispose();
        Rendering.Integration.RenderingCleanup.Run(Reset, lifetime.Dispose);
    }
    #endregion

    #region Private
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
        geometry ??= new(CelestialRenderer.FullscreenLayout, PrimitiveType.Triangles,
            new Dictionary<int, GpuVbo>(), proceduralVertices: 3);
        publication = lighting; frameIndex = VgeFrameRenderer.Current.FrameIndex; admitted = true;
    }
    /// <summary>Adds the guarded HDR disk and solar metadata while preserving primary depth and caller state.</summary>
    private void Draw()
    {
        if (frameIndex != VgeFrameRenderer.Current.FrameIndex || !ReferenceEquals(publication, AtmosphereModSystem.Lighting))
            throw new InvalidOperationException("Solar publication changed after frame admission.");
        var primary = api.Render.FrameBuffers[(int)EnumFrameBuffer.Primary];
        var target = gbuffer.PrimaryFramebuffer;
        if (primary is null || target.FboId != primary.FboId || api.Render.CurrentFrameBuffer?.FboId != primary.FboId
            || !target.HasRenderPassMetadata) return;
        var program = GpuShaderProgramLibraries.Get<SunProgram>(api, "celestial_sun")!;
        // Glare settings are captured later in Before, but are complete before any Opaque draw.
        var glare = Postprocessing.PostprocessPipeline.GlareStrengths;
        float maximumScale = AtmosphereSolarDiskStorage.MaximumScale(AtmosphereSolarDisk.Radiance(publication!), glare.Bloom, glare.Shafts);
        if (maximumScale <= 0) throw new InvalidOperationException("Solar storage headroom could not be certified.");
        program.DrawInputs.Capture(publication!, maximumScale);
        var pass = new RenderPassDesc(target, [new(0), new(1)]);
        using var metadata = new RenderPassTargets(pass);
        var blend = new ColorBlendDesc { Enabled = true, SourceRgb = BlendingFactorSrc.One, DestinationRgb = BlendingFactorDest.One,
            SourceAlpha = BlendingFactorSrc.Zero, DestinationAlpha = BlendingFactorDest.One };
        var description = new GraphicsPipelineDesc(program.GraphicsIdentity!, CelestialRenderer.FullscreenLayout,
            metadata.Signature, DynamicPipelineState.Viewport, blending: [blend, blend]);
        if (pipeline is null || pipeline.Description != description || pipeline.ExecutableRevision != program.ExecutableRevision)
        {
            var candidate = new GraphicsPipeline(lifetime, description, program); pipeline?.Dispose(); pipeline = candidate;
        }
        if (!GraphicsCommandContext.TryRun("Celestial.Sun", [pipeline], true, commands =>
        {
            commands.BeginPass(pass); commands.SetPipeline(pipeline);
            commands.SetDynamicState(new() { Viewport = commands.PassViewport });
            commands.Draw(geometry!, new(0, 3)); commands.EndPass();
        })) throw new InvalidOperationException("Solar graphics boundary was rejected.");
    }
    /// <summary>Withdraws frame admission and retires resources without restoring native callbacks.</summary>
    private void Reset()
    {
        admitted = false; failed = false; publication = null;
        Rendering.Integration.RenderingCleanup.Run(
            () => { pipeline?.Dispose(); pipeline = null; }, () => { geometry?.Dispose(); geometry = null; });
    }
    /// <summary>Retries admission after shader reload without changing renderer registration.</summary>
    private bool Reload() { Reset(); return true; }
    #endregion
}
