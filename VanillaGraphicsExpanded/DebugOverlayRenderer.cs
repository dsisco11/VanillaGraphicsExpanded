using System;

using Vintagestory.API.Client;
using Vintagestory.API.MathTools;

namespace VanillaGraphicsExpanded;

/// <summary>
/// Renders a small debug overlay in the corner of the screen showing various G-buffer visualizations.
/// </summary>
public sealed class DebugOverlayRenderer : IRenderer, IDisposable
{
    #region Constants

    private const double RENDER_ORDER = 0.96; // After PBR overlay
    private const int RENDER_RANGE = 1;
    private const int DEBUG_MODE_COUNT = 6;
    
    // Overlay size and position (normalized 0-1)
    private const float OVERLAY_SIZE = 0.25f; // 25% of screen
    private const float OVERLAY_MARGIN = 0.02f; // 2% margin from edge

    #endregion

    #region Fields

    private readonly ICoreClientAPI capi;
    private readonly GBufferRenderer gBufferRenderer;
    private MeshRef? quadMeshRef;
    private IShaderProgram? shaderProgram;
    private int debugMode;
    private bool isEnabled;
    private readonly float[] invProjectionMatrix = new float[16];
    private readonly float[] invModelViewMatrix = new float[16];

    #endregion

    #region IRenderer Implementation

    public double RenderOrder => RENDER_ORDER;
    public int RenderRange => RENDER_RANGE;

    #endregion

    #region Constructor

    public DebugOverlayRenderer(ICoreClientAPI capi, GBufferRenderer gBufferRenderer)
    {
        this.capi = capi;
        this.gBufferRenderer = gBufferRenderer;

        // Create quad mesh for bottom-left corner
        // Position in NDC: bottom-left is (-1,-1), we want a small quad there
        float left = -1.0f + OVERLAY_MARGIN * 2;
        float bottom = -1.0f + OVERLAY_MARGIN * 2;
        float size = OVERLAY_SIZE * 2; // NDC is -1 to 1, so multiply by 2
        
        var quadMesh = QuadMeshUtil.GetCustomQuadModelData(left, bottom, 0, size, size);
        quadMesh.Rgba = null;
        quadMeshRef = capi.Render.UploadMesh(quadMesh);

        // Register for shader reload events
        capi.Event.ReloadShader += LoadShader;
        LoadShader();

        // Register renderer
        capi.Event.RegisterRenderer(this, EnumRenderStage.AfterBlit, "debugoverlay");

        // Register hotkey
        capi.Input.RegisterHotKey(
            "vgedebugoverlay",
            "VGE Debug Overlay",
            GlKeys.F6,
            HotkeyType.DevTool);
        capi.Input.SetHotKeyHandler("vgedebugoverlay", OnDebugHotkey);
    }

    #endregion

    #region Shader Loading

    private bool LoadShader()
    {
        // Use the same pbroverlay shader, just render it in a small quad with debug mode set
        shaderProgram = capi.Shader.NewShaderProgram();
        shaderProgram.AssetDomain = "vanillagraphicsexpanded";
        shaderProgram.VertexShader = capi.Shader.NewShader(EnumShaderType.VertexShader);
        shaderProgram.FragmentShader = capi.Shader.NewShader(EnumShaderType.FragmentShader);

        capi.Shader.RegisterFileShaderProgram("pbroverlay", shaderProgram);
        var success = shaderProgram.Compile();
        if (!success)
        {
            capi.Logger.Error("[VGE] Failed to compile debug overlay shader");
        }

        return success;
    }

    #endregion

    #region Hotkey Handling

    private bool OnDebugHotkey(KeyCombination keyCombination)
    {
        if (!isEnabled)
        {
            isEnabled = true;
            debugMode = 1; // Start at 1 (Normals), 0 is PBR output
        }
        else
        {
            debugMode++;
            if (debugMode >= DEBUG_MODE_COUNT)
            {
                isEnabled = false;
                debugMode = 1;
            }
        }

        if (isEnabled)
        {
            // Debug modes match pbroverlay.fsh: 1=normals, 2=roughness, 3=metallic, 4=worldPos, 5=depth
            string modeName = debugMode switch
            {
                1 => "Normals",
                2 => "Roughness",
                3 => "Metallic",
                4 => "World Position",
                5 => "Depth",
                _ => "Unknown"
            };
            capi.ShowChatMessage($"[VGE] Debug: {modeName}");
        }
        else
        {
            capi.ShowChatMessage("[VGE] Debug overlay disabled");
        }

        return true;
    }

    #endregion

    #region Rendering

    public void OnRenderFrame(float deltaTime, EnumRenderStage stage)
    {
        if (!isEnabled || shaderProgram is null || quadMeshRef is null)
        {
            return;
        }

        var primaryFb = capi.Render.FrameBuffers[(int)EnumFrameBuffer.Primary];

        // Compute inverse matrices
        ComputeInverseMatrix(capi.Render.CurrentProjectionMatrix, invProjectionMatrix);
        ComputeInverseMatrix(capi.Render.CameraMatrixOriginf, invModelViewMatrix);

        var camPos = capi.World.Player.Entity.CameraPos;
        const double ModuloRange = 4096.0;
        
        var cameraOriginFloor = new Vec3f(
            (float)(Math.Floor(camPos.X) % ModuloRange),
            (float)(Math.Floor(camPos.Y) % ModuloRange),
            (float)(Math.Floor(camPos.Z) % ModuloRange));
        
        var cameraOriginFrac = new Vec3f(
            (float)(camPos.X - Math.Floor(camPos.X)),
            (float)(camPos.Y - Math.Floor(camPos.Y)),
            (float)(camPos.Z - Math.Floor(camPos.Z)));

        // Disable depth writing/testing for overlay
        capi.Render.GLDepthMask(false);
        capi.Render.GlToggleBlend(false);

        shaderProgram.Use();

        // Bind textures
        shaderProgram.BindTexture2D("primaryScene", primaryFb.ColorTextureIds[0], 0);
        shaderProgram.BindTexture2D("primaryDepth", primaryFb.DepthTextureId, 1);
        shaderProgram.BindTexture2D("gBufferNormal", gBufferRenderer.NormalTextureId, 2);

        // Pass uniforms (same as PBROverlayRenderer)
        shaderProgram.UniformMatrix("invProjectionMatrix", invProjectionMatrix);
        shaderProgram.UniformMatrix("invModelViewMatrix", invModelViewMatrix);
        shaderProgram.Uniform("zNear", capi.Render.ShaderUniforms.ZNear);
        shaderProgram.Uniform("zFar", capi.Render.ShaderUniforms.ZFar);
        shaderProgram.Uniform("frameSize", new Vec2f(capi.Render.FrameWidth, capi.Render.FrameHeight));
        shaderProgram.Uniform("cameraOriginFloor", cameraOriginFloor);
        shaderProgram.Uniform("cameraOriginFrac", cameraOriginFrac);
        shaderProgram.Uniform("sunDirection", capi.Render.ShaderUniforms.LightPosition3D);
        shaderProgram.Uniform("debugMode", debugMode);

        // Render the small quad
        capi.Render.RenderMesh(quadMeshRef);

        shaderProgram.Stop();

        // Restore state
        capi.Render.GLDepthMask(true);
    }

    #endregion

    #region Matrix Utilities

    private static void ComputeInverseMatrix(float[] m, float[] result)
    {
        float[] aug = new float[32];
        for (int i = 0; i < 4; i++)
        {
            for (int j = 0; j < 4; j++)
            {
                aug[i * 8 + j] = m[i * 4 + j];
                aug[i * 8 + j + 4] = (i == j) ? 1.0f : 0.0f;
            }
        }

        for (int col = 0; col < 4; col++)
        {
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

            if (maxRow != col)
            {
                for (int j = 0; j < 8; j++)
                {
                    (aug[col * 8 + j], aug[maxRow * 8 + j]) = (aug[maxRow * 8 + j], aug[col * 8 + j]);
                }
            }

            float pivot = aug[col * 8 + col];
            if (Math.Abs(pivot) < 1e-10f)
            {
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
