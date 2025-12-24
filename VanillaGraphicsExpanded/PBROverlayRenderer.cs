using System;
using Vintagestory.API.Client;
using Vintagestory.API.MathTools;

namespace VanillaGraphicsExpanded;

/// <summary>
/// Fullscreen overlay renderer that applies PBR-style lighting to the rendered scene
/// using procedurally generated roughness/metallic values from world position hashing.
/// </summary>
public sealed class PBROverlayRenderer : IRenderer
{
    #region Constants

    private const double RENDER_ORDER = 0.95;
    private const int RENDER_RANGE = 1;
    private const int DEBUG_MODE_COUNT = 6;

    #endregion

    #region Fields

    private readonly ICoreClientAPI capi;
    private MeshRef? quadMeshRef;
    private IShaderProgram? shaderProgram;
    private int debugMode;
    private readonly float[] invProjectionMatrix = new float[16];
    private readonly float[] invModelViewMatrix = new float[16];

    #endregion

    #region IRenderer Implementation

    public double RenderOrder => RENDER_ORDER;
    public int RenderRange => RENDER_RANGE;

    #endregion

    #region Constructor

    public PBROverlayRenderer(ICoreClientAPI capi)
    {
        this.capi = capi;

        // Create fullscreen quad mesh (-1,-1) to (1,1) in NDC
        var quadMesh = QuadMeshUtil.GetCustomQuadModelData(-1, -1, 0, 2, 2);
        quadMesh.Rgba = null;
        quadMeshRef = capi.Render.UploadMesh(quadMesh);

        // Register for shader reload events
        capi.Event.ReloadShader += LoadShader;
        LoadShader();

        // Register renderer at AfterBlit stage
        capi.Event.RegisterRenderer(this, EnumRenderStage.AfterBlit, "pbroverlay");

        // Register debug mode hotkey
        capi.Input.RegisterHotKey(
            "pbroverlaydebug",
            "PBR Overlay Debug Mode",
            GlKeys.F6,
            HotkeyType.DevTool);
        capi.Input.SetHotKeyHandler("pbroverlaydebug", OnDebugModeHotkey);
    }

    #endregion

    #region Shader Loading

    private bool LoadShader()
    {
        shaderProgram = capi.Shader.NewShaderProgram();
        shaderProgram.AssetDomain = "vanillagraphicsexpanded";
        shaderProgram.VertexShader = capi.Shader.NewShader(EnumShaderType.VertexShader);
        shaderProgram.FragmentShader = capi.Shader.NewShader(EnumShaderType.FragmentShader);

        int programId = capi.Shader.RegisterFileShaderProgram("pbroverlay", shaderProgram);
        var success = shaderProgram.Compile();
        if (!success)
        {
            capi.Logger.Error("[VanillaGraphicsExpanded] Failed to compile PBR overlay shader");
        }

        return success;
    }

    #endregion

    #region Hotkey Handling

    private bool OnDebugModeHotkey(KeyCombination keyCombination)
    {
        debugMode = (debugMode + 1) % DEBUG_MODE_COUNT;

        string modeName = debugMode switch
        {
            0 => "PBR Output",
            1 => "Normals",
            2 => "Roughness",
            3 => "Metallic",
            4 => "World Position",
            5 => "Depth",
            _ => "Unknown"
        };

        capi.ShowChatMessage($"PBR Debug Mode: {debugMode} ({modeName})");
        return true;
    }

    #endregion

    #region Rendering

    public void OnRenderFrame(float deltaTime, EnumRenderStage stage)
    {
        if (shaderProgram is null || quadMeshRef is null)
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
        var sunPos = capi.Render.ShaderUniforms.LightPosition3D;

        // Disable depth writing for fullscreen pass
        capi.Render.GLDepthMask(false);
        capi.Render.GlToggleBlend(false);

        shaderProgram.Use();

        // Bind textures
        shaderProgram.BindTexture2D("primaryScene", primaryFb.ColorTextureIds[0], 0);
        shaderProgram.BindTexture2D("primaryDepth", primaryFb.DepthTextureId, 1);

        // Pass matrices
        shaderProgram.UniformMatrix("invProjectionMatrix", invProjectionMatrix);
        shaderProgram.UniformMatrix("invModelViewMatrix", invModelViewMatrix);

        // Pass z-planes
        shaderProgram.Uniform("zNear", capi.Render.ShaderUniforms.ZNear);
        shaderProgram.Uniform("zFar", capi.Render.ShaderUniforms.ZFar);

        // Pass frame size
        shaderProgram.Uniform("frameSize", new Vec2f(capi.Render.FrameWidth, capi.Render.FrameHeight));

        // Pass camera origin for world position reconstruction and sun direction
        shaderProgram.Uniform("cameraOriginFloor", cameraOriginFloor);
        shaderProgram.Uniform("cameraOriginFrac", cameraOriginFrac);
        shaderProgram.Uniform("sunDirection", sunPos);

        // Pass debug mode
        shaderProgram.Uniform("debugMode", debugMode);

        // Render fullscreen quad
        capi.Render.RenderMesh(quadMeshRef);

        shaderProgram.Stop();

        // Restore state
        capi.Render.GLDepthMask(true);
    }

    #endregion

    #region Matrix Utilities

    /// <summary>
    /// Computes the inverse of a 4x4 matrix using Gaussian elimination.
    /// </summary>
    private static void ComputeInverseMatrix(float[] m, float[] result)
    {
        // Create augmented matrix [M | I]
        float[] aug = new float[32];
        for (int i = 0; i < 4; i++)
        {
            for (int j = 0; j < 4; j++)
            {
                aug[i * 8 + j] = m[i * 4 + j];
                aug[i * 8 + j + 4] = (i == j) ? 1.0f : 0.0f;
            }
        }

        // Gaussian elimination with partial pivoting
        for (int col = 0; col < 4; col++)
        {
            // Find pivot
            int maxRow = col;
            float maxVal = Math.Abs(aug[col * 8 + col]);
            for (int row = col + 1; row < 4; row++)
            {
                float val = Math.Abs(aug[row * 8 + col]);
                if (val > maxVal)
                {
                    maxVal = val;
                    maxRow = row;
                }
            }

            // Swap rows
            if (maxRow != col)
            {
                for (int j = 0; j < 8; j++)
                {
                    (aug[col * 8 + j], aug[maxRow * 8 + j]) = (aug[maxRow * 8 + j], aug[col * 8 + j]);
                }
            }

            // Scale pivot row
            float pivot = aug[col * 8 + col];
            if (Math.Abs(pivot) < 1e-10f)
            {
                // Matrix is singular, return identity
                for (int i = 0; i < 16; i++)
                {
                    result[i] = (i % 5 == 0) ? 1.0f : 0.0f;
                }
                return;
            }

            for (int j = 0; j < 8; j++)
            {
                aug[col * 8 + j] /= pivot;
            }

            // Eliminate column
            for (int row = 0; row < 4; row++)
            {
                if (row != col)
                {
                    float factor = aug[row * 8 + col];
                    for (int j = 0; j < 8; j++)
                    {
                        aug[row * 8 + j] -= factor * aug[col * 8 + j];
                    }
                }
            }
        }

        // Extract result
        for (int i = 0; i < 4; i++)
        {
            for (int j = 0; j < 4; j++)
            {
                result[i * 4 + j] = aug[i * 8 + j + 4];
            }
        }
    }

    #endregion

    #region IDisposable

    public void Dispose()
    {
        capi.Event.ReloadShader -= LoadShader;

        if (quadMeshRef is not null)
        {
            capi.Render.DeleteMesh(quadMeshRef);
            quadMeshRef = null;
        }

        shaderProgram?.Dispose();
        shaderProgram = null;
    }

    #endregion
}
