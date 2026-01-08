using System;
using System.Numerics;

using Vintagestory.API.Client;
using Vintagestory.API.MathTools;
using Vintagestory.Client.NoObf;
using VanillaGraphicsExpanded.Rendering;

using static OpenTK.Graphics.OpenGL.GL;

namespace VanillaGraphicsExpanded;

/// <summary>
/// Fullscreen overlay renderer that applies PBR-style lighting to the rendered scene
/// using procedurally generated roughness/metallic values from world position hashing.
/// </summary>
public class PBROverlayRenderer : IRenderer, IDisposable
{
    #region Constants
    private const double RENDER_ORDER = 1.0;
    private const int RENDER_RANGE = 1;
    #endregion

    #region Fields
    protected readonly ICoreClientAPI capi;
    protected readonly GBufferManager gBufferManager;
    protected MeshRef? quadMeshRef;
    protected readonly float[] invProjectionMatrix = new float[16];
    protected readonly float[] invModelViewMatrix = new float[16];

    /// <summary>
    /// Normal blur sample count (Teardown-style): 0=off, 4, 8, 12, 16.
    /// Higher values produce smoother edges but cost more performance.
    /// </summary>
    public int NormalQuality { get; set; } = 8;

    /// <summary>
    /// Normal blur radius in pixels (typically 1.0-3.0).
    /// Larger values create more pronounced beveled edge effect.
    /// </summary>
    public float NormalBlurRadius { get; set; } = 12.0f;

    /// <summary>
    /// Distance (in blocks) where procedural PBR values start to fade out.
    /// </summary>
    public float PbrFalloffStart { get; set; } = 10.0f;

    /// <summary>
    /// Distance (in blocks) where procedural PBR values fully fade to defaults.
    /// </summary>
    public float PbrFalloffEnd { get; set; } = 30.0f;

    /// <summary>
    /// Whether the PBR overlay is enabled. Can be toggled with F7 hotkey.
    /// </summary>
    public bool Enabled { get; set; } = true;
    #endregion

    #region IRenderer Implementation
    public virtual double RenderOrder => RENDER_ORDER;
    public virtual int RenderRange => RENDER_RANGE;
    #endregion

    #region Constructor

    public PBROverlayRenderer(ICoreClientAPI capi, GBufferManager gBufferManager)
        : this(capi, gBufferManager, -1, -1, 2, "pbroverlay")
    {
    }

    /// <summary>
    /// Protected constructor for subclasses to customize quad geometry and render stage name.
    /// </summary>
    /// <param name="capi">Client API</param>
    /// <param name="gBufferManager">G-buffer manager for texture access</param>
    /// <param name="quadLeft">Left edge of quad in NDC (-1 to 1)</param>
    /// <param name="quadBottom">Bottom edge of quad in NDC (-1 to 1)</param>
    /// <param name="quadSize">Size of quad in NDC units</param>
    /// <param name="renderStageName">Name for renderer registration</param>
    protected PBROverlayRenderer(ICoreClientAPI capi, GBufferManager gBufferManager,
        float quadLeft, float quadBottom, float quadSize, string renderStageName)
    {
        this.capi = capi;
        this.gBufferManager = gBufferManager;

        // Create quad mesh with specified geometry
        var quadMesh = QuadMeshUtil.GetCustomQuadModelData(quadLeft, quadBottom, 0, quadSize, quadSize);
        quadMesh.Rgba = null;
        quadMeshRef = capi.Render.UploadMesh(quadMesh);

        // Register renderer
        //capi.Event.RegisterRenderer(this, EnumRenderStage.Opaque, renderStageName);
        //capi.Event.RegisterRenderer(this, EnumRenderStage.OIT, renderStageName);
        //capi.Event.RegisterRenderer(this, EnumRenderStage.AfterOIT, renderStageName);
        //capi.Event.RegisterRenderer(this, EnumRenderStage.ShadowNearDone, renderStageName);
        capi.Event.RegisterRenderer(this, EnumRenderStage.AfterBlit, renderStageName);

        // Register hotkey to toggle PBR overlay (F7)
        capi.Input.RegisterHotKey(
            "vgetoggle",
            "VGE Toggle PBR Overlay",
            GlKeys.F7,
            HotkeyType.DevTool);
        capi.Input.SetHotKeyHandler("vgetoggle", OnTogglePbrOverlay);
    }

    private bool OnTogglePbrOverlay(KeyCombination keyCombination)
    {
        this.Enabled = !this.Enabled;

        string status = this.Enabled ? "enabled" : "disabled";
        capi?.TriggerIngameError(this, "vge", $"[VGE] PBR overlay {status}");

        return true;
    }

    #endregion

    #region Virtual Hooks

    /// <summary>
    /// Override to control whether rendering should occur this frame.
    /// </summary>
    protected virtual bool ShouldRender() => Enabled;

    /// <summary>
    /// Override to return a debug visualization mode (0 = normal PBR output).
    /// </summary>
    protected virtual int GetDebugMode() => 0;

    #endregion

    #region Rendering

    public void OnRenderFrame(float deltaTime, EnumRenderStage stage)
    {
        if (quadMeshRef is null || !ShouldRender())
        {
            return;
        }

        //if (stage == EnumRenderStage.OIT)
        //{// Ensure G-buffer is detatched so it doesnt get cleared by OIT renderer
        //    gBufferRenderer.DetachGBuffer();
        //    return;
        //}

        var primaryFb = capi.Render.FrameBuffers[(int)EnumFrameBuffer.Primary];

        // Compute inverse matrices
        MatrixHelper.Invert(capi.Render.CurrentProjectionMatrix, invProjectionMatrix);
        MatrixHelper.Invert(capi.Render.CameraMatrixOriginf, invModelViewMatrix);

        // The inverse view matrix (CameraMatrixOriginf) gives positions relative to camera.
        // To get world position, we need to add the camera's absolute world position.
        // For float precision with large coordinates, use modulo and split into floor + frac.
        var camPos = capi.World.Player.Entity.CameraPos;
        const double ModuloRange = 4096.0;
        
        // Floor-aligned camera position (only changes when crossing block boundaries)
        var cameraOriginFloor = new Vec3f(
            (float)(Math.Floor(camPos.X) % ModuloRange),
            (float)(Math.Floor(camPos.Y) % ModuloRange),
            (float)(Math.Floor(camPos.Z) % ModuloRange));
        
        // Fractional part of camera position (sub-block offset)
        var cameraOriginFrac = new Vec3f(
            (float)(camPos.X - Math.Floor(camPos.X)),
            (float)(camPos.Y - Math.Floor(camPos.Y)),
            (float)(camPos.Z - Math.Floor(camPos.Z)));

        // Get sun direction from shader uniforms
        // Use SunPosition3D for specular to match the visual sun position
        // (LightPosition3D may blend sun/moon or have other adjustments)
        var sunPos = capi.Render.ShaderUniforms.SunPosition3D;

        // Disable depth writing for fullscreen pass
        capi.Render.GLDepthMask(false);
        capi.Render.GlToggleBlend(false);

        
        //var shader = PBROverlayShaderProgram.Instance;
        var shader = ShaderRegistry.getProgramByName("pbroverlay") as PBROverlayShaderProgram;
        if (shader is null)
        {
            return;
        }

        shader.Use();

        // Bind textures
        shader.PrimaryScene = primaryFb.ColorTextureIds[0];
        shader.PrimaryDepth = primaryFb.DepthTextureId;
        shader.GBufferNormal = gBufferManager.NormalTextureId;
        shader.GBufferMaterial = gBufferManager.MaterialTextureId;

        // Pass matrices
        shader.InvProjectionMatrix = invProjectionMatrix;
        shader.InvModelViewMatrix = invModelViewMatrix;

        // Pass z-planes
        shader.ZNear = capi.Render.ShaderUniforms.ZNear;
        shader.ZFar = capi.Render.ShaderUniforms.ZFar;

        // Pass frame size
        shader.FrameSize = new Vec2f(capi.Render.FrameWidth, capi.Render.FrameHeight);

        // Pass camera origin for world position reconstruction and sun direction
        shader.CameraOriginFloor = cameraOriginFloor;
        shader.CameraOriginFrac = cameraOriginFrac;
        shader.LightDirection = sunPos;

        // Use virtual method for debug mode (0 = PBR output, subclasses can override)
        shader.DebugMode = GetDebugMode();

        // Normal blur settings (Teardown-style golden ratio spiral sampling)
        shader.NormalQuality = NormalQuality;
        shader.NormalBlurRadius = NormalBlurRadius;

        // PBR distance falloff settings
        shader.PbrFalloffStart = PbrFalloffStart;
        shader.PbrFalloffEnd = PbrFalloffEnd;

        // Lighting
        shader.RgbaLightIn = ColorUtil.WhiteArgbVec.XYZ;
        shader.RgbaAmbientIn = capi.Render.AmbientColor;

        // Render fullscreen quad
        capi.Render.RenderMesh(quadMeshRef);

        shader.Stop();

        // Restore state
        capi.Render.GLDepthMask(true);
    }

    #endregion

    #region Matrix Utilities

    // Matrix utilities moved to VanillaGraphicsExpanded.Rendering.MatrixHelper

    #endregion

    #region IDisposable

    public virtual void Dispose()
    {
        if (quadMeshRef is not null)
        {
            capi.Render.DeleteMesh(quadMeshRef);
            quadMeshRef = null;
        }
    }

    #endregion
}
