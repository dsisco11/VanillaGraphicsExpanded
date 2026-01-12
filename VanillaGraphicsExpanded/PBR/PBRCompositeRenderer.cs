using System;

using OpenTK.Graphics.OpenGL;

using Vintagestory.API.Client;
using Vintagestory.API.MathTools;
using Vintagestory.Client.NoObf;

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

        var shader = ShaderRegistry.getProgramByName("pbr_composite") as PBRCompositeShaderProgram;
        if (shader is null || shader.LoadError)
        {
            return;
        }

        // Matrices for optional PBR composite mode
        MatrixHelper.Invert(capi.Render.CurrentProjectionMatrix, invProjectionMatrix);
        Array.Copy(capi.Render.CameraMatrixOriginf, viewMatrix, 16);

        // Bind primary framebuffer so subsequent base-game post-processing sees the combined result.
        GL.BindFramebuffer(FramebufferTarget.Framebuffer, primaryFb.FboId);
        GL.Viewport(0, 0, capi.Render.FrameWidth, capi.Render.FrameHeight);

        // IMPORTANT: Restrict output to ColorAttachment0.
        GL.DrawBuffer(DrawBufferMode.ColorAttachment0);

        capi.Render.GlToggleBlend(false);

        shader.Use();

        // Direct lighting radiance buffers (linear, fog-free)
        shader.DirectDiffuse = directLightingBuffers.DirectDiffuseTex.TextureId;
        shader.DirectSpecular = directLightingBuffers.DirectSpecularTex.TextureId;
        shader.Emissive = directLightingBuffers.EmissiveTex.TextureId;

        // Indirect diffuse (LumOn) - optional
        int lumOnEnabled = 0;
        int indirectTexId = 0;
        if (lumOnConfig?.Enabled == true && lumOnBuffers?.IndirectFullTex is not null)
        {
            lumOnEnabled = 1;
            indirectTexId = lumOnBuffers.IndirectFullTex.TextureId;
        }

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

        shader.LumOnEnabled = lumOnEnabled;

        // Phase 15 knobs (keep existing behavior)
        shader.EnablePbrComposite = lumOnConfig?.EnablePbrComposite == true ? 1 : 0;
        shader.EnableAO = lumOnConfig?.EnableAO == true ? 1 : 0;
        shader.EnableBentNormal = lumOnConfig?.EnableBentNormal == true ? 1 : 0;
        shader.DiffuseAOStrength = Math.Clamp(lumOnConfig?.DiffuseAOStrength ?? 1.0f, 0f, 1f);
        shader.SpecularAOStrength = Math.Clamp(lumOnConfig?.SpecularAOStrength ?? 1.0f, 0f, 1f);

        shader.InvProjectionMatrix = invProjectionMatrix;
        shader.ViewMatrix = viewMatrix;

        using (GlGpuProfiler.Instance.Scope("PBR.Composite"))
        {
            capi.Render.RenderMesh(quadMeshRef);
        }

        shader.Stop();

        // Leave primary bound for subsequent in-stage passes.
    }

    public void Dispose()
    {
        if (quadMeshRef is not null)
        {
            capi.Render.DeleteMesh(quadMeshRef);
            quadMeshRef = null;
        }

        capi.Event.UnregisterRenderer(this, EnumRenderStage.Opaque);
    }
}
