using HarmonyLib;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.Rendering.Pipeline;

namespace VanillaGraphicsExpanded.Tests.GPU.Fixtures;

/// <summary>Observes actual sampler bindings immediately before existing procedural cloud draws.</summary>
internal sealed class CloudArrayBindingCapture:IDisposable
{
    [ThreadStatic] private static CloudArrayBindingCapture? active;
    private readonly Harmony harmony=new("Tests.Cloud.ArrayBindingCapture");
    internal readonly List<(int Framebuffer,int Current,int History)> Draws=[];
    /// <summary>Selects a one-based submission at which to inject a failure; negative values disable injection.</summary>
    internal int FailAtDraw=-1;
    #region Public API
    /// <summary>Installs one test-scoped observer without replacing draw execution or production resources.</summary>
    internal CloudArrayBindingCapture()
    {
        if(active is not null)throw new InvalidOperationException("A binding capture is already active.");
        active=this;
        harmony.Patch(AccessTools.Method(typeof(ArrayGraphicsGeometry),"Submit"),prefix:new HarmonyMethod(AccessTools.Method(typeof(CloudArrayBindingCapture),nameof(Observe))));
    }
    /// <summary>Reads array bindings while preserving the inherited active texture unit.</summary>
    internal static (int Current,int History) ReadBindings()
    {
        int previous=GL.GetInteger(GetPName.ActiveTexture);
        try
        {
            GL.ActiveTexture(TextureUnit.Texture0);int current=GL.GetInteger(GetPName.TextureBinding2DArray);
            GL.ActiveTexture(TextureUnit.Texture7);return(current,GL.GetInteger(GetPName.TextureBinding2DArray));
        }
        finally { GL.ActiveTexture((TextureUnit)previous); }
    }
    /// <summary>Removes the observer even when an assertion or production draw fails.</summary>
    public void Dispose(){harmony.UnpatchAll(harmony.Id);active=null;}
    #endregion
    #region Private
    /// <summary>Snapshots the state actually visible to the driver after typed submission has published its resources.</summary>
    private static void Observe()
    {
        if(active is null)return;
        var bindings=ReadBindings();active.Draws.Add((GL.GetInteger(GetPName.DrawFramebufferBinding),bindings.Current,bindings.History));
        if(active.Draws.Count==active.FailAtDraw)throw new InvalidOperationException("Injected cloud submission failure.");
    }
    #endregion
}
