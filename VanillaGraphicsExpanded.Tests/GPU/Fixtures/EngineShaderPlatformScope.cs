using System.Reflection;
using System.Runtime.CompilerServices;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.Rendering;
using Vintagestory.API.Client;
using Vintagestory.Client;
using Vintagestory.Client.NoObf;

namespace VanillaGraphicsExpanded.Tests.GPU.Fixtures;

/// <summary>Supplies the engine-owned uniform service used by ShaderProgramBase.Use without creating a game window.</summary>
internal sealed class EngineShaderPlatformScope : IDisposable
{
    private readonly ClientPlatformAbstract? previous=ScreenManager.Platform;
    private readonly ShaderProgramBase? previousShader=ShaderProgramBase.CurrentShaderProgram;

    #region Engine shader boundary
    /// <summary>Initializes only the uniform property; the fixture never invokes platform rendering, audio or window methods.</summary>
    public EngineShaderPlatformScope()
    {
        // The concrete engine getter only reads this property. Avoid its constructor, which starts native subsystems.
        var platform=(ClientPlatformWindows)RuntimeHelpers.GetUninitializedObject(typeof(ClientPlatformWindows));
        typeof(ClientPlatformWindows).GetProperty(nameof(ClientPlatformWindows.ShaderUniforms))!.SetValue(platform,new DefaultShaderUniforms());
        ScreenManager.Platform=platform;
        ShaderProgramBase.CurrentShaderProgram=null;
    }

    /// <summary>Restores both engine and GL program ownership even if a production upload throws.</summary>
    public void Dispose()
    {
        ShaderProgramBase.CurrentShaderProgram=previousShader;
        ScreenManager.Platform=previous;
        GL.UseProgram(previousShader?.ProgramId??0);
        GlStateCache.Current.NotifyProgramBound(previousShader?.ProgramId??0);
    }
    #endregion
}
