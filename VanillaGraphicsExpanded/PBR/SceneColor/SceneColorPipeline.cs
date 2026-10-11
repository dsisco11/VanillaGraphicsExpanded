using System;
using VanillaGraphicsExpanded.ModSystems;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Rendering.Shaders;
using Vintagestory.Client;
using Vintagestory.Client.NoObf;
using Vintagestory.API.Client;

namespace VanillaGraphicsExpanded.PBR.SceneColor;

/// <summary>Prepares the mandatory HDR scene producers and tracks their pending display input.</summary>
internal sealed class SceneColorPipeline : IRenderer
{
    private static SceneColorPipeline? active;
    private readonly ICoreClientAPI api;
    private readonly PBRCompositeRenderer composite;
    private readonly DirectLightingRenderer direct;
    private readonly DirectLightingBufferManager lightingTargets;
    private readonly Atmosphere.AtmosphereSkyRenderer sky;
    private readonly SceneColorParticleCapture particles;
    private readonly SceneColorFrameTargets targets = new();
    private readonly Action unregisterResize;
    private bool hasSceneInput;

    #region Public API
    /// <summary>Replaces pre-mod scene storage through the engine owner before VGE borrows its attachments.</summary>
    internal static void InitializeStorage(ICoreClientAPI api)
    {
        var initialTargets = new SceneColorFrameTargets();
        if (initialTargets.Prepare(api.Render.FrameBuffers, out _)) return;

        // The engine allocates menu framebuffers before mod patches are installed.
        // Rebuild through its owner so the allocation hook applies, replacement images
        // are published, old storage is retired and existing dependents are notified.
        ScreenManager.Platform.RebuildFrameBuffers();
        initialTargets.Invalidate();
        if (!initialTargets.Prepare(api.Render.FrameBuffers, out string? failure))
            throw new InvalidOperationException($"VGE HDR could not initialize scene storage: {failure}");
    }

    /// <summary>Registers preparation after atmospheric publication and particle reset, before scene drawing.</summary>
    internal SceneColorPipeline(ICoreClientAPI api, PBRCompositeRenderer composite,
        DirectLightingRenderer direct, DirectLightingBufferManager lightingTargets,
        Atmosphere.AtmosphereSkyRenderer sky, SceneColorParticleCapture particles)
    {
        this.api = api;
        this.composite = composite;
        this.direct = direct;
        this.lightingTargets = lightingTargets;
        this.sky = sky;
        this.particles = particles;
        active = this;
        unregisterResize = ScreenResourceManager.Register(ScreenResourceManager.CompositeOrder, Reset);
        api.Event.RegisterRenderer(this, EnumRenderStage.Before, "vge_scene_color");
        api.Event.LeaveWorld += RetireScene;
        api.Event.ReloadShader += Reload;
    }

    /// <summary>Runs after existing Before-stage resource and atmospheric publication.</summary>
    public double RenderOrder => 1000;
    /// <summary>Scene preparation does not depend on visibility distance.</summary>
    public int RenderRange => int.MaxValue;
    /// <summary>Distinguishes pending scene postprocessing from unrelated menu or offscreen processing.</summary>
    internal static bool HasSceneInput => active?.hasSceneInput == true;

    /// <summary>Prepares required HDR resources and reports missing dependencies without changing scene color space.</summary>
    public void OnRenderFrame(float deltaTime, EnumRenderStage stage)
    {
        if (stage != EnumRenderStage.Before) return;
        // This tracks the input to final composition, not an optional HDR mode. Even
        // failed preparation must never cause subsequent scene draws to emit display RGB.
        hasSceneInput = true;
        var final = GpuShaderPrograms.Get<Postprocessing.FinalDisplayShaderProgram>(api, "pbr_final");
        if (final?.EnsureReady() != true)
            throw new InvalidOperationException($"VGE HDR requires pbr_final: {final?.PreparationFailure?.Message ?? "declaration unavailable or not ready"}.", final?.PreparationFailure);
        if (!targets.Prepare(api.Render.FrameBuffers, out string? failure))
            throw new InvalidOperationException($"VGE HDR scene storage is invalid: {failure}");
        if (AtmosphereModSystem.Lighting is null || AtmosphereModSystem.SkyTextureId == 0
            || !sky.PrepareFrame())
            throw new InvalidOperationException("VGE HDR atmospheric sky resources are unavailable.");
        if (direct.PrepareBoundaryPipeline() is null
            || !lightingTargets.EnsureBuffers(api.Render.FrameWidth, api.Render.FrameHeight)
            || !composite.PrepareFrame())
            throw new InvalidOperationException("VGE HDR deferred lighting resources are unavailable.");
        if (!particles.PrepareFrame(true))
            throw new InvalidOperationException("VGE HDR particle separation resources are unavailable.");
    }

    /// <summary>Consumes the pending scene input after its display endpoint, including exceptional exits.</summary>
    internal static void EndScene()
    {
        if (active is not null) active.hasSceneInput = false;
    }

    /// <summary>Unregisters the selection boundary before its dependencies are retired.</summary>
    public void Dispose()
    {
        if (ReferenceEquals(active, this)) active = null;
        api.Event.UnregisterRenderer(this, EnumRenderStage.Before);
        api.Event.LeaveWorld -= RetireScene;
        api.Event.ReloadShader -= Reload;
        unregisterResize();
        RetireScene();
    }
    #endregion

    #region Private
    /// <summary>Invalidates borrowed metadata without changing the convention of already submitted scene pixels.</summary>
    private void Reset() => targets.Invalidate();

    /// <summary>Retires pending world input so unrelated menu processing remains outside scene exposure.</summary>
    private void RetireScene()
    {
        hasSceneInput = false;
        targets.Invalidate();
    }

    /// <summary>Requires fresh producer preparation following shader reload.</summary>
    private bool Reload()
    {
        Reset();
        return true;
    }
    #endregion
}
