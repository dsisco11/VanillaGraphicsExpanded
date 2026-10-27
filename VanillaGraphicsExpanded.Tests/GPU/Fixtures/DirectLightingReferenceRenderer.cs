using VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;
using VanillaGraphicsExpanded.Tests.GPU.Fixtures;
using VanillaGraphicsExpanded.Rendering.RHI;
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

        var shader = Rendering.Shaders.GpuShaderProgramLibraries.Get<PBRDirectLightingShaderProgram>(capi, "pbr_direct_lighting");
        if (shader != null) Rendering.Shaders.GpuShaderProgramLibraries.Preload(capi, [shader]);

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
        OpenGlStateCache.Current.RequireOutsideEngineBoundary();
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
        var shader = GpuShaderProgramLibraries.Get<PBRDirectLightingShaderProgram>(capi, "pbr_direct_lighting");
        return shader?.EnsureReady() == true ? shader : null;
    }

    /// <summary>Draws shared lighting work without nesting an engine restoration boundary.</summary>
    internal bool RenderLightingWithinBoundary(EngineBoundaryScope scope, DirectLightingTargets? isolated = null)
    {
        OpenGlStateCache.Current.RequireEngineBoundary(scope);
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
        var shader = global::VanillaGraphicsExpanded.Rendering.Shaders.GpuShaderProgramLibraries.Get<PBRDirectLightingShaderProgram>(capi, "pbr_direct_lighting");
        if (shader is null || shader.RequiresPreparation || shader.IsRetired)
        {
            return false;
        }

        // Bind output MRT FBO
        OpenGlStateCache.Current.Apply(LightingPipeline);
        var radiance = isolated?.Radiance ?? bufferManager.Radiance!;
        using var diffuse = GpuFramebufferAttachment.FromTexture(radiance, layer: 0);
        using var specular = GpuFramebufferAttachment.FromTexture(radiance, layer: 1);
        using var emission = GpuFramebufferAttachment.FromTexture(radiance, layer: 2);
        using var target = DynamicGpuFramebuffer.Create([diffuse, specular, emission])!;
        FramebufferTestSetup.BindForEngineRendering(target);
        for (int output=0; output<3; output++) OpenTK.Graphics.OpenGL.GL.ClearBuffer(OpenTK.Graphics.OpenGL.ClearBuffer.Color,output,new float[4]);



        // Bind input textures
        using var primaryColor = new BorrowedTexture(primaryFb.ColorTextureIds[0]);
        using var primaryDepth = new BorrowedTexture(primaryFb.DepthTextureId);
        shader.PrimaryScene = primaryColor;
        shader.PrimaryDepth = SceneColor.SceneColorParticleCapture.ReceiverDepth(capi, primaryDepth);
        shader.GBufferSurface = gBufferManager.SurfaceTexture;
        using var position = new BorrowedTexture(gBufferManager.PositionTextureId);
        shader.GBufferPosition = position;

        // Shadow maps (depth textures)
        var shadowNearFb = capi.Render.FrameBuffers[(int)EnumFrameBuffer.ShadowmapNear];
        var shadowFarFb = capi.Render.FrameBuffers[(int)EnumFrameBuffer.ShadowmapFar];
        using var shadowNear = shadowNearFb is null ? null : new BorrowedTexture(shadowNearFb.DepthTextureId);
        using var shadowFar = shadowFarFb is null ? null : new BorrowedTexture(shadowFarFb.DepthTextureId);
        shader.ShadowMapNear = shadowNear;
        shader.ShadowMapFar = shadowFar;

        // Matrices
        using var frameCamera = TestFrameCamera.CreateFromInverseView(invProjectionMatrix, invModelViewMatrix, target.Width, target.Height,
            capi.Render.ShaderUniforms.ZNear, capi.Render.ShaderUniforms.ZFar);
        shader.FrameInputs = frameCamera;
        shader.ToShadowMapSpaceMatrixNear = capi.Render.ShaderUniforms.ToShadowMapSpaceMatrixNear;
        shader.ToShadowMapSpaceMatrixFar = capi.Render.ShaderUniforms.ToShadowMapSpaceMatrixFar;

        // Lighting
        shader.RgbaAmbientIn = capi.Render.AmbientColor;
        var atmosphere = ModSystems.AtmosphereModSystem.Lighting;
        shader.SetSolarLighting(atmosphere?.Sun ?? System.Numerics.Vector3.UnitY,
            atmosphere?.Solar ?? System.Numerics.Vector3.Zero);

        // Vanilla supplies view-space point lights; the shader compares them with view-space receivers.
        using var sharedLights = new VgeLightsUniformBuffer();
        sharedLights.Capture(
            capi.Render.ShaderUniforms.PointLightsCount,
            capi.Render.ShaderUniforms.PointLights3,
            capi.Render.ShaderUniforms.PointLightColors3);
        shader.LightsInputs = sharedLights;

        // Shadow params
        shader.ShadowRangeNear = capi.Render.ShaderUniforms.ShadowRangeNear;
        shader.ShadowRangeFar = capi.Render.ShaderUniforms.ShadowRangeFar;
        shader.ShadowZExtendNear = capi.Render.ShaderUniforms.ShadowZExtendNear;
        shader.ShadowZExtendFar = capi.Render.ShaderUniforms.ShadowZExtendFar;
        shader.DropShadowIntensity = capi.Render.ShaderUniforms.DropShadowIntensity;

        using var cpuScope = Profiler.BeginScope("PBR.DirectLighting", "Render");
        using (GlGpuProfiler.Instance.Scope("PBR.DirectLighting"))
        {
            using var bindingPipeline = ShaderBindingTestPipeline.Create(shader);
            RHICommandContext.Current.SetPipeline(bindingPipeline);
            RHICommandContext.Current.BindResources(shader.CaptureBindings());
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
