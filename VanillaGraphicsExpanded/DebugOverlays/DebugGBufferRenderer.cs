using System;
using OpenTK.Graphics.OpenGL;
using Vintagestory.API.Client;
using Vintagestory.API.MathTools;

namespace VanillaGraphicsExpanded;

/// <summary>
/// Renders a fullscreen debug overlay that blits the selected G-buffer attachment to the screen.
/// Uses the built-in blit shader for simple texture passthrough.
/// </summary>
public sealed class DebugGBufferRenderer : IRenderer, IDisposable
{
    #region Constants

    private const double DEBUG_RENDER_ORDER = 1; // After PBR overlay
    private const int DEBUG_MODE_COUNT = 6; // 1-5 are valid debug modes

    #endregion

    #region Fields

    private readonly ICoreClientAPI capi;
    private readonly GBufferManager gBufferManager;
    private MeshRef? quadMeshRef;
    private int debugMode = 1;
    private bool isEnabled;

    #endregion

    #region Properties

    public double RenderOrder => DEBUG_RENDER_ORDER;
    public int RenderRange => 1;

    #endregion

    #region Constructor

    public DebugGBufferRenderer(ICoreClientAPI capi, GBufferManager gBufferManager)
    {
        this.capi = capi;
        this.gBufferManager = gBufferManager;

        // Create fullscreen quad mesh (-1 to 1 in NDC)
        var quadMesh = QuadMeshUtil.GetCustomQuadModelData(-1, -1, 0, 2, 2);
        quadMesh.Rgba = null;
        quadMeshRef = capi.Render.UploadMesh(quadMesh);

        // Register hotkey for debug mode cycling (F6 forward, Shift+F6 backward)
        capi.Input.RegisterHotKey(
            "vgegbufferoverlay",
            "VGE GBuffer Overlay",
            GlKeys.F8,
            HotkeyType.DevTool);
        capi.Input.SetHotKeyHandler("vgegbufferoverlay", OnDebugHotkey);

        capi.Input.RegisterHotKey(
            "vgegbufferoverlayprev",
            "VGE GBuffer Overlay (Previous)",
            GlKeys.F8,
            HotkeyType.DevTool,
            shiftPressed: true);
        capi.Input.SetHotKeyHandler("vgegbufferoverlayprev", OnDebugHotkeyPrev);

        // Register renderer at AfterBlit stage
        capi.Event.RegisterRenderer(this, EnumRenderStage.AfterBlit, "debugoverlay");
    }

    #endregion

    #region Rendering

    public void OnRenderFrame(float deltaTime, EnumRenderStage stage)
    {
        if (!isEnabled || quadMeshRef is null)
        {
            return;
        }

        // Get the texture ID for the current debug mode
        int textureId = GetTextureForDebugMode();
        if (textureId == 0)
        {
            return;
        }

        // Disable depth test/write for fullscreen blit
        capi.Render.GLDepthMask(false);
        capi.Render.GlToggleBlend(false);
        GL.Disable(EnableCap.DepthTest);

        // Use the built-in blit shader for simple passthrough
        var blitShader = capi.Render.GetEngineShader(EnumShaderProgram.Blit);
        blitShader.Use();

        // Bind the G-buffer texture to sample from
        capi.Render.GlToggleBlend(false);
        GL.ActiveTexture(TextureUnit.Texture0);
        GL.BindTexture(TextureTarget.Texture2D, textureId);
        blitShader.BindTexture2D("scene", textureId, 0);

        // Render fullscreen quad
        capi.Render.RenderMesh(quadMeshRef);

        blitShader.Stop();

        // Restore state
        GL.Enable(EnableCap.DepthTest);
        capi.Render.GLDepthMask(true);
    }

    private int GetTextureForDebugMode()
    {
        return debugMode switch
        {
            1 => gBufferManager.NormalTextureId,
            2 => gBufferManager.MaterialTextureId,
            3 => capi.Render.FrameBuffers[(int)EnumFrameBuffer.Primary].DepthTextureId,
            4 => capi.Render.FrameBuffers[(int)EnumFrameBuffer.Primary].ColorTextureIds[0],
            _ => 0
        };
    }

    #endregion

    #region Hotkey Handling

    private bool OnDebugHotkey(KeyCombination keyCombination)
    {
        if (!isEnabled)
        {
            isEnabled = true;
            debugMode = 1;
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

        ShowDebugModeMessage();
        return true;
    }

    private bool OnDebugHotkeyPrev(KeyCombination keyCombination)
    {
        if (!isEnabled)
        {
            isEnabled = true;
            debugMode = DEBUG_MODE_COUNT - 1;
        }
        else
        {
            debugMode--;
            if (debugMode < 1)
            {
                isEnabled = false;
                debugMode = 1;
            }
        }

        ShowDebugModeMessage();
        return true;
    }

    private void ShowDebugModeMessage()
    {
        if (isEnabled)
        {
            string modeName = debugMode switch
            {
                1 => "G-Buffer: Normals",
                2 => "G-Buffer: Material",
                3 => "G-Buffer: Albedo",
                4 => "Depth Buffer",
                5 => "Primary Color",
                _ => "Unknown"
            };
            capi.TriggerIngameError(this, "vgedebug", $"Debug: {modeName}");
        }
        else
        {
            capi.TriggerIngameError(this, "vgedebug", "Debug overlay disabled");
        }
    }

    #endregion

    #region IDisposable

    public void Dispose()
    {
        if (quadMeshRef is not null)
        {
            capi.Render.DeleteMesh(quadMeshRef);
            quadMeshRef = null;
        }
    }

    #endregion
}
