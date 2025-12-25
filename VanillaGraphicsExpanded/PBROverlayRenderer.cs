using System;
using System.Numerics;

using Vintagestory.API.Client;
using Vintagestory.API.MathTools;

namespace VanillaGraphicsExpanded;

/// <summary>
/// Fullscreen overlay renderer that applies PBR-style lighting to the rendered scene
/// using procedurally generated roughness/metallic values from world position hashing.
/// </summary>
public class PBROverlayRenderer : IRenderer, IDisposable
{
    #region Constants

    private const double RENDER_ORDER = 0.95;
    private const int RENDER_RANGE = 1;

    #endregion

    #region Fields

    protected readonly ICoreClientAPI capi;
    protected readonly GBufferRenderer gBufferRenderer;
    protected MeshRef? quadMeshRef;
    protected PBROverlayShaderProgram? shader;
    protected readonly float[] invProjectionMatrix = new float[16];
    protected readonly float[] invModelViewMatrix = new float[16];

    /// <summary>
    /// Normal blur sample count (Teardown-style): 0=off, 4, 8, 12, 16.
    /// Higher values produce smoother edges but cost more performance.
    /// </summary>
    public int NormalQuality { get; set; } = 16;

    /// <summary>
    /// Normal blur radius in pixels (typically 1.0-3.0).
    /// Larger values create more pronounced beveled edge effect.
    /// </summary>
    public float NormalBlurRadius { get; set; } = 6.0f;

    /// <summary>
    /// Distance (in blocks) where procedural PBR values start to fade out.
    /// </summary>
    public float PbrFalloffStart { get; set; } = 10.0f;

    /// <summary>
    /// Distance (in blocks) where procedural PBR values fully fade to defaults.
    /// </summary>
    public float PbrFalloffEnd { get; set; } = 30.0f;

    #endregion

    #region IRenderer Implementation

    public virtual double RenderOrder => RENDER_ORDER;
    public virtual int RenderRange => RENDER_RANGE;

    #endregion

    #region Constructor

    public PBROverlayRenderer(ICoreClientAPI capi, GBufferRenderer gBufferRenderer)
        : this(capi, gBufferRenderer, -1, -1, 2, "pbroverlay")
    {
    }

    /// <summary>
    /// Protected constructor for subclasses to customize quad geometry and render stage name.
    /// </summary>
    /// <param name="capi">Client API</param>
    /// <param name="gBufferRenderer">G-buffer renderer for normal texture access</param>
    /// <param name="quadLeft">Left edge of quad in NDC (-1 to 1)</param>
    /// <param name="quadBottom">Bottom edge of quad in NDC (-1 to 1)</param>
    /// <param name="quadSize">Size of quad in NDC units</param>
    /// <param name="renderStageName">Name for renderer registration</param>
    protected PBROverlayRenderer(ICoreClientAPI capi, GBufferRenderer gBufferRenderer,
        float quadLeft, float quadBottom, float quadSize, string renderStageName)
    {
        this.capi = capi;
        this.gBufferRenderer = gBufferRenderer;

        // Create quad mesh with specified geometry
        var quadMesh = QuadMeshUtil.GetCustomQuadModelData(quadLeft, quadBottom, 0, quadSize, quadSize);
        quadMesh.Rgba = null;
        quadMeshRef = capi.Render.UploadMesh(quadMesh);

        // Register for shader reload events
        capi.Event.ReloadShader += LoadShader;
        LoadShader();

        // Register renderer at AfterBlit stage
        capi.Event.RegisterRenderer(this, EnumRenderStage.AfterBlit, renderStageName);
    }

    #endregion

    #region Shader Loading

    private bool LoadShader()
    {
        shader = new PBROverlayShaderProgram();
        shader.PassName = "pbroverlay";
        shader.AssetDomain = "vanillagraphicsexpanded";
        capi.Shader.RegisterFileShaderProgram("pbroverlay", shader);
        var success = shader.Compile();
        if (!success)
        {
            capi.Logger.Error("[VanillaGraphicsExpanded] Failed to compile PBR overlay shader");
            return false;
        }

        return true;
    }

    #endregion

    #region Virtual Hooks

    /// <summary>
    /// Override to control whether rendering should occur this frame.
    /// </summary>
    protected virtual bool ShouldRender() => true;

    /// <summary>
    /// Override to return a debug visualization mode (0 = normal PBR output).
    /// </summary>
    protected virtual int GetDebugMode() => 0;

    #endregion

    #region Rendering

    public void OnRenderFrame(float deltaTime, EnumRenderStage stage)
    {
        if (shader is null || quadMeshRef is null || !ShouldRender())
        {
            return;
        }

        var primaryFb = capi.Render.FrameBuffers[(int)EnumFrameBuffer.Primary];

        // Compute inverse matrices
        ComputeInverseMatrix(capi.Render.CurrentProjectionMatrix, invProjectionMatrix);
        ComputeInverseMatrix(capi.Render.CameraMatrixOriginf, invModelViewMatrix);

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

        shader.Use();

        // Bind textures
        shader.PrimaryScene = primaryFb.ColorTextureIds[0];
        shader.PrimaryDepth = primaryFb.DepthTextureId;
        shader.GBufferNormal = gBufferRenderer.NormalTextureId;

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

    /// <summary>
    /// Computes the inverse of a 4x4 matrix using SIMD-accelerated System.Numerics.
    /// </summary>
    private static void ComputeInverseMatrix(float[] m, float[] result)
    {
        // Convert to Matrix4x4 (row-major constructor matches OpenGL column-major layout when transposed)
        var matrix = new Matrix4x4(
            m[0], m[4], m[8], m[12],
            m[1], m[5], m[9], m[13],
            m[2], m[6], m[10], m[14],
            m[3], m[7], m[11], m[15]);

        if (!Matrix4x4.Invert(matrix, out var inverse))
        {
            // Matrix is singular, return identity
            for (int i = 0; i < 16; i++)
            {
                result[i] = (i % 5 == 0) ? 1.0f : 0.0f;
            }
            return;
        }

        // Convert back to column-major float array (OpenGL layout)
        result[0] = inverse.M11;
        result[1] = inverse.M21;
        result[2] = inverse.M31;
        result[3] = inverse.M41;
        result[4] = inverse.M12;
        result[5] = inverse.M22;
        result[6] = inverse.M32;
        result[7] = inverse.M42;
        result[8] = inverse.M13;
        result[9] = inverse.M23;
        result[10] = inverse.M33;
        result[11] = inverse.M43;
        result[12] = inverse.M14;
        result[13] = inverse.M24;
        result[14] = inverse.M34;
        result[15] = inverse.M44;
    }

    #endregion

    #region IDisposable

    public virtual void Dispose()
    {
        capi.Event.ReloadShader -= LoadShader;

        if (quadMeshRef is not null)
        {
            capi.Render.DeleteMesh(quadMeshRef);
            quadMeshRef = null;
        }

        shader?.Dispose();
        shader = null;
    }

    #endregion
}
