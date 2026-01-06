using Vintagestory.Client.NoObf;

namespace VanillaGraphicsExpanded.SSGI;

/// <summary>
/// Simple shader program for debug visualization of the SSGI buffer.
/// Renders the SSGI texture directly to screen with optional boost.
/// </summary>
public class SSGIDebugShaderProgram : ShaderProgram
{
    #region Static

    public static void Register(Vintagestory.API.Client.ICoreClientAPI api)
    {
        var instance = new SSGIDebugShaderProgram
        {
            PassName = "ssgi_debug",
            AssetDomain = "vanillagraphicsexpanded"
        };
        api.Shader.RegisterFileShaderProgram("ssgi_debug", instance);
        instance.Compile();
    }

    #endregion

    #region Uniforms

    /// <summary>
    /// The SSGI texture to display (texture unit 0).
    /// </summary>
    public int SSGITexture { set => BindTexture2D("ssgiTexture", value, 0); }

    /// <summary>
    /// Brightness boost multiplier for visibility.
    /// </summary>
    public float Boost { set => Uniform("boost", value); }

    #endregion
}
