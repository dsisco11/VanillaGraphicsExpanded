using System;

using OpenTK.Graphics.OpenGL;

using Vintagestory.API.Client;
using Vintagestory.API.MathTools;
using Vintagestory.Client.NoObf;

using VanillaGraphicsExpanded.Profiling;
using VanillaGraphicsExpanded.LumOn;
using VanillaGraphicsExpanded.ModSystems;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Rendering.Profiling;

namespace VanillaGraphicsExpanded.PBR;

/// <summary>
/// Final composite pass that merges direct radiance buffers (diffuse/specular/emissive)
/// with optional indirect lighting (LumOn) and applies fog once.
/// Writes the result into the primary framebuffer ColorAttachment0 so base-game
/// post-processing sees the combined result.
/// </summary>
public sealed class PBRCompositeRenderer : IRenderer, IDisposable
{
    private const double RenderOrderValue = 11.0;
    private const int RenderRangeValue = 1;

    // Neither fullscreen draw may test or overwrite the primary terrain depth.
    private static readonly GlPipelineDesc CompositePipeline = new(
        defaultMask: GlPipelineStateMask.From(GlPipelineStateId.DepthTestEnable)
            .With(GlPipelineStateId.BlendEnable)
            .With(GlPipelineStateId.CullFaceEnable)
            .With(GlPipelineStateId.ScissorTestEnable)
            .With(GlPipelineStateId.ColorMask),
        nonDefaultMask: GlPipelineStateMask.From(GlPipelineStateId.DepthWriteMask),
        depthWriteMask: false,
        name: "PBR.CompositeAndDisplay");

    private readonly ICoreClientAPI capi;
    private readonly GBufferManager gBufferManager;
    private readonly DirectLightingBufferManager directLightingBuffers;
    private readonly VgeConfig? lumOnConfig;
    private readonly Func<LumOnBufferManager?> getLumOnBuffers;
    private readonly Func<bool> readLightingMode;

    private MeshRef? quadMeshRef;

    private GpuFramebuffer? compositeFbo;
    private DynamicTexture2D? compositeColorTex;

    /// <summary>Pre-display lighting retained by this renderer; sky pixels retain the engine's color convention.</summary>
    internal GpuTexture? SceneLinearColor => compositeColorTex;

    private readonly float[] invProjectionMatrix = new float[16];
    private readonly float[] viewMatrix = new float[16];

    public double RenderOrder => RenderOrderValue;

    public int RenderRange => RenderRangeValue;

    /// <summary>Creates the fullscreen mesh and registers composition after direct and indirect lighting.</summary>
    public PBRCompositeRenderer(
        ICoreClientAPI capi,
        GBufferManager gBufferManager,
        DirectLightingBufferManager directLightingBuffers,
        VgeConfig? lumOnConfig,
        Func<LumOnBufferManager?> getLumOnBuffers,
        Func<bool>? readLightingMode = null)
    {
        this.capi = capi;
        this.gBufferManager = gBufferManager;
        this.directLightingBuffers = directLightingBuffers;
        this.lumOnConfig = lumOnConfig;
        this.getLumOnBuffers = getLumOnBuffers;
        this.readLightingMode = readLightingMode ?? (() => lumOnConfig?.LumOn.Enabled == true);

        var quadMesh = QuadMeshUtil.GetCustomQuadModelData(-1, -1, 0, 2, 2);
        quadMesh.Rgba = null;
        quadMeshRef = capi.Render.UploadMesh(quadMesh);

        capi.Event.RegisterRenderer(this, EnumRenderStage.Opaque, "pbr_composite");

        capi.Logger.Notification("[VGE] PBRCompositeRenderer registered (Opaque @ 11.0)");
    }

    /// <summary>Combines scene-linear lighting, then resolves it into the engine's display target.</summary>
    public void OnRenderFrame(float deltaTime, EnumRenderStage stage)
    {
        if (stage != EnumRenderStage.Opaque || quadMeshRef is null)
        {
            return;
        }

        int screenW = capi.Render.FrameWidth;
        int screenH = capi.Render.FrameHeight;
        if (screenW <= 0 || screenH <= 0)
        {
            return;
        }

        var primaryFb = capi.Render.FrameBuffers[(int)EnumFrameBuffer.Primary];
        if (primaryFb is null)
        {
            return;
        }

        // Need direct pass outputs.
        if (directLightingBuffers.DirectDiffuseTex is null
            || directLightingBuffers.DirectSpecularTex is null
            || directLightingBuffers.EmissiveTex is null)
        {
            return;
        }

        var shader = global::VanillaGraphicsExpanded.Rendering.Shaders.GpuShaderPrograms.Get<PBRCompositeShaderProgram>(capi, "pbr_composite");
        if (shader is null)
        {
            return;
        }

        // Ensure the scratch composite target exists and matches current resolution.
        // Use existing GBuffer/DynamicTexture resize support (no custom ensure helper).
        if (compositeFbo is null || compositeColorTex is null || !compositeFbo.IsValid || !compositeColorTex.IsValid)
        {
            compositeFbo?.Dispose();
            compositeFbo = null;

            compositeColorTex?.Dispose();
            compositeColorTex = null;

            compositeColorTex = DynamicTexture2D.Create(screenW, screenH, PixelInternalFormat.Rgba16f, debugName: "PBRComposite");
            if (!compositeColorTex.IsValid)
            {
                compositeColorTex.Dispose();
                compositeColorTex = null;
                return;
            }

            compositeFbo = GpuFramebuffer.CreateSingle(compositeColorTex, depthTexture: null, ownsTextures: false, debugName: "PBRCompositeFBO");
            if (compositeFbo is null || !compositeFbo.IsValid)
            {
                compositeFbo?.Dispose();
                compositeFbo = null;
                compositeColorTex.Dispose();
                compositeColorTex = null;
                return;
            }
        }
        else
        {
            // Resizes attached textures in-place when resolution changes.
            compositeFbo.Resize(screenW, screenH);
        }

        // Define-backed toggles must be set before Use() so the correct variant is bound.
        bool lumOnEnabled = readLightingMode();
        var currentBuffers = lumOnEnabled ? getLumOnBuffers() : null;
        var indirectTex = currentBuffers?.HasPublishedIndirect == true ? currentBuffers.IndirectFullTex : null;
        if (indirectTex?.IsValid != true) indirectTex = null;

        shader.ConfigureOptions(() =>
        {
            shader.LumOnEnabled = lumOnEnabled;
            shader.EnablePbrComposite = lumOnEnabled && (lumOnConfig?.LumOn.EnablePbrComposite ?? true);
            shader.EnableShortRangeAo = lumOnEnabled && (lumOnConfig?.LumOn.EnableShortRangeAo ?? true);
        });

        var display = global::VanillaGraphicsExpanded.Rendering.Shaders.GpuShaderPrograms.Get<PBRDisplayResolveShaderProgram>(capi, "pbr_display_resolve");
        if (!shader.EnsureReady() || display is null || !display.EnsureReady()) return;

        // Matrices for optional PBR composite mode
        MatrixHelper.Invert(capi.Render.CurrentProjectionMatrix, invProjectionMatrix);
        Array.Copy(capi.Render.CameraMatrixOriginf, viewMatrix, 16);

        // Render into a scratch buffer to avoid sampling from the same texture we're writing to
        // (Primary ColorAttachment0 is also used as gBufferAlbedo / primaryScene input).
        using var fixedFunctionScope = GlStateCache.Current.CaptureLegacyFixedFunctionState();
        GlStateCache.Current.InvalidateAll();
        GlStateCache.Current.Apply(CompositePipeline);
        compositeFbo!.Bind();
        GL.Viewport(0, 0, screenW, screenH);

        // Single target output.
        GL.DrawBuffer(DrawBufferMode.ColorAttachment0);

        if (!shader.TryUse()) return;

        // Direct lighting radiance buffers (linear, fog-free)
        shader.DirectDiffuse = directLightingBuffers.DirectDiffuseTex;
        shader.DirectSpecular = directLightingBuffers.DirectSpecularTex;
        shader.Emissive = directLightingBuffers.EmissiveTex;

        if (lumOnEnabled) shader.IndirectDiffuse = indirectTex;
        shader.GBufferEnvironment = gBufferManager.EnvironmentTextureId;

        // GBuffer inputs
        shader.GBufferAlbedo = primaryFb.ColorTextureIds[0];
        shader.GBufferMaterial = gBufferManager.MaterialTextureId;
        shader.GBufferNormal = gBufferManager.NormalTextureId;
        shader.PrimaryDepth = primaryFb.DepthTextureId;

        // Fog uniforms
        shader.RgbaFogIn = capi.Render.FogColor;
        shader.FogDensityIn = capi.Render.FogDensity;
        shader.FogMinIn = capi.Render.FogMin;
        shader.SetAtmosphere(AtmosphereModSystem.Lighting);

        // The published LumOn gather output already includes intensity and tint.
        // Composition applies receiver material response without scaling that signal twice.
        shader.IndirectIntensity = indirectTex is not null ? 1.0f : 0.0f;
        shader.IndirectTint = new Vec3f(1, 1, 1);

        shader.DiffuseAOStrength = Math.Clamp(lumOnConfig?.LumOn.DiffuseAOStrength ?? 1.0f, 0f, 1f);
        shader.SpecularAOStrength = Math.Clamp(lumOnConfig?.LumOn.SpecularAOStrength ?? 1.0f, 0f, 1f);

        shader.InvProjectionMatrix = invProjectionMatrix;
        shader.ViewMatrix = viewMatrix;

        using var cpuScope = Profiler.BeginScope("PBR.Composite", "Render");
        using (GlGpuProfiler.Instance.Scope("PBR.Composite"))
        {
            capi.Render.RenderMesh(quadMeshRef);
        }

        shader.Stop();

        // Resolve HDR deliberately before the RGBA8 primary and vanilla display grading.
        // The source is our scratch texture, so this draw has no color attachment feedback.
        GlStateCache.Current.BindFramebuffer(FramebufferTarget.Framebuffer, primaryFb.FboId);
        GL.Viewport(0, 0, screenW, screenH);
        GL.DrawBuffer(DrawBufferMode.ColorAttachment0);
        if (!display.TryUse()) return;
        display.PrimaryScene = compositeColorTex!.TextureId;
        display.PrimaryDepth = primaryFb.DepthTextureId;
        using (GlGpuProfiler.Instance.Scope("PBR.DisplayResolve"))
        {
            capi.Render.RenderMesh(quadMeshRef);
        }
        display.Stop();
    }

    /// <summary>Releases owned fullscreen resources and unregisters the renderer.</summary>
    public void Dispose()
    {
        if (quadMeshRef is not null)
        {
            capi.Render.DeleteMesh(quadMeshRef);
            quadMeshRef = null;
        }

        compositeFbo?.Dispose();
        compositeFbo = null;

        compositeColorTex?.Dispose();
        compositeColorTex = null;

        capi.Event.UnregisterRenderer(this, EnumRenderStage.Opaque);
    }
}
