using System;

using OpenTK.Graphics.OpenGL;

using Vintagestory.API.Client;
using Vintagestory.API.MathTools;
using Vintagestory.Client.NoObf;

using VanillaGraphicsExpanded.Profiling;
using VanillaGraphicsExpanded.LumOn;
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

    private readonly ICoreClientAPI capi;
    private readonly GBufferManager gBufferManager;
    private readonly DirectLightingBufferManager directLightingBuffers;
    private readonly LumOnConfig? lumOnConfig;
    private readonly LumOnBufferManager? lumOnBuffers;

    private MeshRef? quadMeshRef;

    private GBuffer? compositeFbo;
    private DynamicTexture? compositeColorTex;

    private readonly float[] invProjectionMatrix = new float[16];
    private readonly float[] viewMatrix = new float[16];

    public double RenderOrder => RenderOrderValue;

    public int RenderRange => RenderRangeValue;

    public PBRCompositeRenderer(
        ICoreClientAPI capi,
        GBufferManager gBufferManager,
        DirectLightingBufferManager directLightingBuffers,
        LumOnConfig? lumOnConfig,
        LumOnBufferManager? lumOnBuffers)
    {
        this.capi = capi;
        this.gBufferManager = gBufferManager;
        this.directLightingBuffers = directLightingBuffers;
        this.lumOnConfig = lumOnConfig;
        this.lumOnBuffers = lumOnBuffers;

        var quadMesh = QuadMeshUtil.GetCustomQuadModelData(-1, -1, 0, 2, 2);
        quadMesh.Rgba = null;
        quadMeshRef = capi.Render.UploadMesh(quadMesh);

        capi.Event.RegisterRenderer(this, EnumRenderStage.Opaque, "pbr_composite");

        capi.Logger.Notification("[VGE] PBRCompositeRenderer registered (Opaque @ 11.0)");
    }

    public void OnRenderFrame(float deltaTime, EnumRenderStage stage)
    {
        if (stage != EnumRenderStage.Opaque || quadMeshRef is null)
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

        var shader = capi.Shader.GetProgramByName("pbr_composite") as PBRCompositeShaderProgram;
        if (shader is null || shader.LoadError)
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

            compositeColorTex = DynamicTexture.Create(capi.Render.FrameWidth, capi.Render.FrameHeight, PixelInternalFormat.Rgba16f, debugName: "PBRComposite");
            if (!compositeColorTex.IsValid)
            {
                compositeColorTex.Dispose();
                compositeColorTex = null;
                return;
            }

            compositeFbo = GBuffer.CreateSingle(compositeColorTex, depthTexture: null, ownsTextures: false, debugName: "PBRCompositeFBO");
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
            compositeFbo.Resize(capi.Render.FrameWidth, capi.Render.FrameHeight);
        }

        // Matrices for optional PBR composite mode
        MatrixHelper.Invert(capi.Render.CurrentProjectionMatrix, invProjectionMatrix);
        Array.Copy(capi.Render.CameraMatrixOriginf, viewMatrix, 16);

        // Render into a scratch buffer to avoid sampling from the same texture we're writing to
        // (Primary ColorAttachment0 is also used as gBufferAlbedo / primaryScene input).
        compositeFbo!.Bind();
        GL.Viewport(0, 0, capi.Render.FrameWidth, capi.Render.FrameHeight);

        // Single target output.
        GL.DrawBuffer(DrawBufferMode.ColorAttachment0);

        capi.Render.GlToggleBlend(false);

        // Define-backed toggles must be set before Use() so the correct variant is bound.
        int lumOnEnabled = 0;
        int indirectTexId = 0;
        if (lumOnConfig?.Enabled == true && lumOnBuffers?.IndirectFullTex is not null)
        {
            lumOnEnabled = 1;
            indirectTexId = lumOnBuffers.IndirectFullTex.TextureId;
        }

        shader.LumOnEnabled = lumOnEnabled == 1;

        // Phase 15 knobs (now compile-time defines)
        shader.EnablePbrComposite = lumOnConfig?.EnablePbrComposite ?? true;
        shader.EnableAO = lumOnConfig?.EnableAO ?? true;
        shader.EnableBentNormal = lumOnConfig?.EnableBentNormal ?? true;

        shader.Use();

        // Direct lighting radiance buffers (linear, fog-free)
        shader.DirectDiffuse = directLightingBuffers.DirectDiffuseTex.TextureId;
        shader.DirectSpecular = directLightingBuffers.DirectSpecularTex.TextureId;
        shader.Emissive = directLightingBuffers.EmissiveTex.TextureId;

        shader.IndirectDiffuse = indirectTexId;

        // GBuffer inputs
        shader.GBufferAlbedo = primaryFb.ColorTextureIds[0];
        shader.GBufferMaterial = gBufferManager.MaterialTextureId;
        shader.GBufferNormal = gBufferManager.NormalTextureId;
        shader.PrimaryDepth = primaryFb.DepthTextureId;

        // Fog uniforms
        shader.RgbaFogIn = capi.Render.FogColor;
        shader.FogDensityIn = capi.Render.FogDensity;
        shader.FogMinIn = capi.Render.FogMin;

        // Indirect controls
        shader.IndirectIntensity = lumOnConfig?.Intensity ?? 1.0f;
        if (lumOnConfig?.IndirectTint is not null && lumOnConfig.IndirectTint.Length >= 3)
        {
            shader.IndirectTint = new Vec3f(lumOnConfig.IndirectTint[0], lumOnConfig.IndirectTint[1], lumOnConfig.IndirectTint[2]);
        }
        else
        {
            shader.IndirectTint = new Vec3f(1, 1, 1);
        }

        shader.DiffuseAOStrength = Math.Clamp(lumOnConfig?.DiffuseAOStrength ?? 1.0f, 0f, 1f);
        shader.SpecularAOStrength = Math.Clamp(lumOnConfig?.SpecularAOStrength ?? 1.0f, 0f, 1f);

        shader.InvProjectionMatrix = invProjectionMatrix;
        shader.ViewMatrix = viewMatrix;

        using var cpuScope = Profiler.BeginScope("PBR.Composite", "Render");
        using (GlGpuProfiler.Instance.Scope("PBR.Composite"))
        {
            capi.Render.RenderMesh(quadMeshRef);
        }

        shader.Stop();

        // Copy composite result into Primary ColorAttachment0 so base-game post-processing sees it.
        compositeFbo.BlitToExternal(
            primaryFb.FboId,
            capi.Render.FrameWidth,
            capi.Render.FrameHeight,
            ClearBufferMask.ColorBufferBit,
            BlitFramebufferFilter.Nearest);

        // Leave primary bound for subsequent in-stage passes.
        GL.BindFramebuffer(FramebufferTarget.Framebuffer, primaryFb.FboId);
        GL.Viewport(0, 0, capi.Render.FrameWidth, capi.Render.FrameHeight);
        GL.DrawBuffer(DrawBufferMode.ColorAttachment0);
    }

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
