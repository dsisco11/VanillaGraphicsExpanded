using SceneColor = VanillaGraphicsExpanded.PBR.SceneColor;
using VanillaGraphicsExpanded.PBR;
using System;
using VanillaGraphicsExpanded.Rendering.Integration;
using VanillaGraphicsExpanded.Rendering.Shaders;


using Vintagestory.API.Client;
using Vintagestory.API.MathTools;
using Vintagestory.Client.NoObf;

using VanillaGraphicsExpanded.Profiling;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Rendering.Profiling;

namespace VanillaGraphicsExpanded.Tests.GPU.Fixtures;

/// <summary>
/// Fullscreen direct lighting pass (Opaque stage).
/// Writes split radiance buffers into DirectLightingBufferManager:
/// - DirectDiffuse
/// - DirectSpecular
/// - Emissive
/// </summary>
internal sealed class DirectLightingReferenceRenderer : IRenderer, IDisposable
{
    private const double RenderOrderValue = 9.0;
    private const int RenderRangeValue = 1;
    internal static readonly GlPipelineDesc LightingPipeline = new(
        defaultMask: GlPipelineStateMask.From(GlPipelineStateId.DepthTestEnable)
            .With(GlPipelineStateId.BlendEnable).With(GlPipelineStateId.CullFaceEnable)
            .With(GlPipelineStateId.ScissorTestEnable).With(GlPipelineStateId.ColorMask),
        nonDefaultMask: GlPipelineStateMask.From(GlPipelineStateId.DepthWriteMask),
        depthWriteMask: false, name: "PBR.DirectLighting");

    private readonly ICoreClientAPI capi;
    private readonly GBufferManager gBufferManager;
    private readonly DirectLightingBufferManager bufferManager;

    private MeshRef? quadMeshRef;

    private readonly float[] invProjectionMatrix = new float[16];
    private readonly float[] invModelViewMatrix = new float[16];

    public double RenderOrder => RenderOrderValue;

    public int RenderRange => RenderRangeValue;

    #region Public API
    #region Rendering
    /// <summary>Registers direct lighting using the engine view-space light coordinates.</summary>
    public DirectLightingReferenceRenderer(
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
        quadMeshRef = capi.Render.UploadMesh(quadMesh);

        capi.Event.RegisterRenderer(this, EnumRenderStage.Opaque, "pbr_direct_lighting");

        capi.Logger.Notification("[VGE] DirectLightingRenderer registered (Opaque @ 9.0)");
    }

    /// <summary>Reconstructs terrain-relative receivers and renders direct lighting into its split targets.</summary>
    public void OnRenderFrame(float deltaTime, EnumRenderStage stage)
    {
        if (stage == EnumRenderStage.Opaque) RenderLighting();
    }

    /// <summary>Evaluates the same lighting contract into an isolated target for a pre-overlay world capture.</summary>
    internal bool RenderLighting(DirectLightingTargets? isolated = null)
    {
        StateCache.Current.RequireOutsideEngineBoundary();
        if (quadMeshRef is null || capi.Render.FrameWidth <= 0 || capi.Render.FrameHeight <= 0) return false;
        var shader = PrepareBoundaryProgram();
        if (shader is null) return false;
        bool rendered = false;
        if (!ReferenceFullscreenBoundary.TryRun("PBR.DirectLighting", [LightingPipeline], [shader],
            scope => rendered = RenderLightingWithinBoundary(scope, isolated)))
            throw new InvalidOperationException("Direct lighting engine boundary unavailable.");
        return rendered;
    }

    /// <summary>Prepares the executable before its resource footprint is captured.</summary>
    internal PBRDirectLightingShaderProgram? PrepareBoundaryProgram()
    {
        var shader = GpuShaderPrograms.Get<PBRDirectLightingShaderProgram>(capi, "pbr_direct_lighting");
        return shader?.EnsureReady() == true ? shader : null;
    }

    /// <summary>Draws shared lighting work without nesting an engine restoration boundary.</summary>
    internal bool RenderLightingWithinBoundary(EngineBoundaryScope scope, DirectLightingTargets? isolated = null)
    {
        StateCache.Current.RequireEngineBoundary(scope);
        if (quadMeshRef is null)
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
        var shader = global::VanillaGraphicsExpanded.Rendering.Shaders.GpuShaderPrograms.Get<PBRDirectLightingShaderProgram>(capi, "pbr_direct_lighting");
        if (shader is null || shader.RequiresPreparation || shader.IsRetired)
        {
            return false;
        }

        // Bind output MRT FBO
        StateCache.Current.Apply(LightingPipeline);
        var target = isolated?.Framebuffer ?? bufferManager.DirectLightingFbo!;
        target.BindWithViewport();
        target.Clear(0, 0, 0, 0);



        // Bind input textures
        shader.PrimaryScene = primaryFb.ColorTextureIds[0];
        shader.PrimaryDepth = SceneColor.SceneColorParticleCapture.ReceiverDepth(capi, primaryFb.DepthTextureId);
        shader.GBufferNormal = gBufferManager.NormalTextureId;
        shader.GBufferPosition = gBufferManager.PositionTextureId;
        shader.GBufferEnvironment = gBufferManager.EnvironmentTextureId;
        shader.GBufferMaterial = gBufferManager.MaterialTextureId;

        // Shadow maps (depth textures)
        var shadowNearFb = capi.Render.FrameBuffers[(int)EnumFrameBuffer.ShadowmapNear];
        var shadowFarFb = capi.Render.FrameBuffers[(int)EnumFrameBuffer.ShadowmapFar];
        if (shadowNearFb != null) shader.ShadowMapNear = shadowNearFb.DepthTextureId;
        if (shadowFarFb != null) shader.ShadowMapFar = shadowFarFb.DepthTextureId;

        // Matrices
        shader.InvProjectionMatrix = invProjectionMatrix;
        shader.InvModelViewMatrix = invModelViewMatrix;
        shader.ToShadowMapSpaceMatrixNear = capi.Render.ShaderUniforms.ToShadowMapSpaceMatrixNear;
        shader.ToShadowMapSpaceMatrixFar = capi.Render.ShaderUniforms.ToShadowMapSpaceMatrixFar;

        // Z planes
        shader.ZNear = capi.Render.ShaderUniforms.ZNear;
        shader.ZFar = capi.Render.ShaderUniforms.ZFar;

        // Lighting
        shader.RgbaAmbientIn = capi.Render.AmbientColor;
        var atmosphere = ModSystems.AtmosphereModSystem.Lighting;
        shader.SetSolarLighting(atmosphere?.Sun ?? System.Numerics.Vector3.UnitY,
            atmosphere?.Solar ?? System.Numerics.Vector3.Zero);

        // Vanilla supplies view-space point lights; the shader compares them with view-space receivers.
        shader.SetPointLights(
            capi.Render.ShaderUniforms.PointLightsCount,
            capi.Render.ShaderUniforms.PointLights3,
            capi.Render.ShaderUniforms.PointLightColors3);

        // Shadow params
        shader.ShadowRangeNear = capi.Render.ShaderUniforms.ShadowRangeNear;
        shader.ShadowRangeFar = capi.Render.ShaderUniforms.ShadowRangeFar;
        shader.ShadowZExtendNear = capi.Render.ShaderUniforms.ShadowZExtendNear;
        shader.ShadowZExtendFar = capi.Render.ShaderUniforms.ShadowZExtendFar;
        shader.DropShadowIntensity = capi.Render.ShaderUniforms.DropShadowIntensity;

        using var cpuScope = Profiler.BeginScope("PBR.DirectLighting", "Render");
        using (GlGpuProfiler.Instance.Scope("PBR.DirectLighting"))
        {
            scope.Activate(shader);
            capi.Render.RenderMesh(quadMeshRef);
        }

        return true;
    }

    #endregion

    #region Lifetime
    /// <summary>Releases the fullscreen mesh and unregisters the lighting callback.</summary>
    public void Dispose()
    {
        if (quadMeshRef is not null)
        {
            capi.Render.DeleteMesh(quadMeshRef);
            quadMeshRef = null;
        }

        capi.Event.UnregisterRenderer(this, EnumRenderStage.Opaque);
    }
    #endregion
    #endregion
}
