using System;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Rendering.Shaders;
using Vintagestory.API.Client;
using Vintagestory.API.MathTools;
using Vintagestory.Client.NoObf;

namespace VanillaGraphicsExpanded.PBR.SceneColor;

/// <summary>Publishes material receiver depth and ordered particle radiance before deferred lighting.</summary>
internal sealed class SceneColorParticleCapture : IRenderer
{
    private static SceneColorParticleCapture? active;
    internal static readonly GlPipelineDesc ResolvePipeline = new(
        defaultMask: GlPipelineStateMask.From(GlPipelineStateId.DepthTestEnable)
            .With(GlPipelineStateId.BlendEnable).With(GlPipelineStateId.CullFaceEnable)
            .With(GlPipelineStateId.ScissorTestEnable).With(GlPipelineStateId.ColorMask),
        nonDefaultMask: GlPipelineStateMask.From(GlPipelineStateId.DepthWriteMask),
        depthWriteMask: false, name: "SceneColor.Particles.Resolve");
    private readonly ICoreClientAPI api;
    private readonly GBufferManager gbuffer;
    private readonly Action unregisterResize;
    private readonly MeshRef quad;
    private SceneColorParticleTargets? targets;
    private SceneColorParticleShaderProgram? resolve;
    private SceneColorParticleSsaoShaderProgram? ssaoRestore;
    private bool prepared;
    private bool published;
    private bool disposed;

    #region Public API
    #region Lifetime
    /// <summary>Registers invalidation and pre-lighting publication without enabling HDR on its own.</summary>
    internal SceneColorParticleCapture(ICoreClientAPI api, GBufferManager gbuffer)
    {
        this.api = api;
        this.gbuffer = gbuffer;
        var mesh = QuadMeshUtil.GetCustomQuadModelData(-1, -1, 0, 2, 2);
        mesh.Rgba = null;
        quad = api.Render.UploadMesh(mesh);
        unregisterResize = ScreenResourceManager.Register(ScreenResourceManager.DirectLightingOrder, Retire);
        api.Event.RegisterRenderer(this, EnumRenderStage.Before, "vge_particle_capture_reset");
        api.Event.RegisterRenderer(this, EnumRenderStage.Opaque, "vge_particle_receiver_resolve");
        api.Event.LeaveWorld += Retire;
        active = this;
    }

    /// <summary>Unregisters publication before disposing owned resources.</summary>
    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        if (ReferenceEquals(active, this)) active = null;
        api.Event.UnregisterRenderer(this, EnumRenderStage.Before);
        api.Event.UnregisterRenderer(this, EnumRenderStage.Opaque);
        api.Event.LeaveWorld -= Retire;
        unregisterResize();
        Retire();
        api.Render.DeleteMesh(quad);
    }
    #endregion

    #region Frame publication
    /// <summary>Resolves after ordinary engine opaque submissions and before direct lighting at order nine.</summary>
    public double RenderOrder => 8.5;
    /// <summary>Runs independently of the visible world range.</summary>
    public int RenderRange => int.MaxValue;

    /// <summary>Prepares every particle dependency before the common HDR owner chooses the frame's convention.</summary>
    internal bool PrepareFrame(bool enabled)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        prepared = published = false;
        targets?.ResetCapture();
        if (!enabled) { Retire(); return false; }
        if (targets is { IsCurrent: false }) Retire();
        if (!GlExtensions.Supports("GL_ARB_clear_texture")) return false;
        var particleProgram = ShaderPrograms.Particlescube;
        if (particleProgram is null
            || !ShaderCapabilities.Has(particleProgram, ShaderCapability.SceneColorConvention)
            || !particleProgram.HasUniform("vge_sceneLinear")) return false;
        resolve = GpuShaderPrograms.Get<SceneColorParticleShaderProgram>(api, "scene_color_particles");
        if (resolve?.EnsureReady() != true) return false;
        var primary = api.Render.FrameBuffers[(int)EnumFrameBuffer.Primary];
        bool hasSsao = primary?.ColorTextureIds is { Length: >= 4 } colors && colors[2] != 0 && colors[3] != 0;
        if (hasSsao)
        {
            ssaoRestore = GpuShaderPrograms.Get<SceneColorParticleSsaoShaderProgram>(api, "scene_color_particle_ssao");
            if (ssaoRestore?.EnsureReady() != true) return false;
        }
        targets ??= new SceneColorParticleTargets(gbuffer.PrimaryFramebuffer, hasSsao);
        prepared = true;
        return true;
    }

    /// <summary>Invalidates old publication each frame and resolves only a completed original particle invocation.</summary>
    public void OnRenderFrame(float deltaTime, EnumRenderStage stage)
    {
        if (disposed) return;
        if (stage == EnumRenderStage.Before)
        {
            prepared = published = false;
            targets?.ResetCapture();
            return;
        }
        if (stage != EnumRenderStage.Opaque || !prepared || targets is not { IsCurrent: true, Captured: true }
            || resolve is null) return;
        // The resolve owns its full-screen state and borrows only read-only source
        // images. Subsequent deferred passes establish their own pipeline state.
        using var bindings = StateCache.Current.BindFramebufferScope();
        targets.ResolveTarget.BindWithViewport();
        StateCache.Current.Apply(ResolvePipeline);
        resolve.VisibilityDepth = targets.VisibilityDepth;
        resolve.BeforeDepth = targets.BeforeDepth;
        resolve.AfterDepth = targets.AfterDepth;
        resolve.ParticleColor = targets.ParticleColor;
        using (resolve.UseScope())
        {
            if (!ReferenceEquals(ShaderProgramBase.CurrentShaderProgram, resolve)) return;
            api.Render.RenderMesh(quad);
        }
        published = true;
    }

    /// <summary>Selects corrected material depth only after current-frame receiver separation has completed.</summary>
    internal static int ReceiverDepth(ICoreClientAPI api, int visibilityDepth)
        => active is { published: true, targets: { IsCurrent: true } } capture && ReferenceEquals(capture.api, api)
            ? capture.targets.ResolveTarget.GetColorTextureId(0) : visibilityDepth;

    /// <summary>Lends the completed current-frame particle layer to the opaque HDR handoff.</summary>
    internal static DynamicTexture2D? Layer(ICoreClientAPI api)
        => active is { published: true, targets: { IsCurrent: true } } capture && ReferenceEquals(capture.api, api)
            ? capture.targets.ResolveTarget[1] : null;

    /// <summary>Resolves the active SSAO helper before the composite captures its resource footprint.</summary>
    internal static GpuProgram? PrepareSsaoBoundary(ICoreClientAPI api)
    {
        if (active is not { published: true, targets: { IsCurrent: true, HasSsao: true }, ssaoRestore: { } shader } capture
            || !ReferenceEquals(capture.api, api)) return null;
        if (!shader.EnsureReady()) throw new InvalidOperationException("SSAO restoration shader unavailable.");
        return shader;
    }

    /// <summary>Returns original particle metadata to engine SSAO only after deferred material composition finishes.</summary>
    internal static void RestoreSsao(ICoreClientAPI api, EngineBoundaryScope? scope = null)
    {
        if (scope is not null) StateCache.Current.RequireEngineBoundary(scope);
        if (active is not { published: true, targets: { IsCurrent: true, HasSsao: true }, ssaoRestore: { } shader } capture
            || !ReferenceEquals(capture.api, api)) return;
        using var bindings = scope is null ? StateCache.Current.BindFramebufferScope() : (StateCache.FramebufferScope?)null;
        capture.targets.SsaoTarget!.BindWithViewport();
        StateCache.Current.Apply(ResolvePipeline);
        shader.VisibilityDepth = capture.targets.VisibilityDepth;
        shader.BeforeDepth = capture.targets.BeforeDepth;
        shader.AfterDepth = capture.targets.AfterDepth;
        shader.ParticleNormal = capture.targets.DrawTarget[2];
        shader.ParticlePosition = capture.targets.DrawTarget[3];
        using var activation = scope is null ? shader.UseScope() : default;
        if (scope is not null) scope.Activate(shader);
        {
            if (!ReferenceEquals(ShaderProgramBase.CurrentShaderProgram, shader)) return;
            api.Render.RenderMesh(capture.quad);
        }
    }
    #endregion

    #region Engine submission
    /// <summary>Wraps only the installed cube-particle opaque draw, never OIT particles or offscreen rendering.</summary>
    internal static SceneColorParticleDrawScope? BeginDraw(int model)
    {
        var program = ShaderPrograms.Particlescube;
        if (model != 1 || program is null || !ReferenceEquals(ShaderProgramBase.CurrentShaderProgram, program)) return null;
        // The same installed program may serve an offscreen or fallback draw.
        // Select its convention at each submission, never retain the preceding frame's input.
        if (program.HasUniform("vge_sceneLinear")) program.Uniform("vge_sceneLinear", 0);
        var capture = active;
        if (capture is not { prepared: true, targets: { IsCurrent: true } }
            || capture.api.Render.CurrentRenderStage != EnumRenderStage.Opaque) return null;
        var primary = capture.api.Render.FrameBuffers[(int)EnumFrameBuffer.Primary];
        if (primary is null || capture.api.Render.CurrentFrameBuffer?.FboId != primary.FboId) return null;
        var scope = new SceneColorParticleDrawScope(capture.targets);
        try { program.Uniform("vge_sceneLinear", 1); return scope; }
        catch { scope.Dispose(); throw; }
    }
    #endregion
    #endregion

    #region Private
    /// <summary>Withdraws all publications before retiring images on disable, resize or world exit.</summary>
    private void Retire()
    {
        prepared = published = false;
        targets?.Dispose();
        targets = null;
        resolve = null;
        ssaoRestore = null;
    }
    #endregion
}
