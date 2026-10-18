using HarmonyLib;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Rendering.Pipeline;

namespace VanillaGraphicsExpanded.Tests.GPU.Fixtures;

/// <summary>Observes published sampler bindings through the state owner before procedural cloud draws.</summary>
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
    /// <summary>Reads published array bindings through the shared state owner.</summary>
    internal static (int Current,int History) ReadBindings()
    {
        var state=StateCache.Current;
        return(state.GetBoundTexture(TextureTarget.Texture2DArray,0),state.GetBoundTexture(TextureTarget.Texture2DArray,7));
    }
    /// <summary>Removes the observer even when an assertion or production draw fails.</summary>
    public void Dispose(){harmony.UnpatchAll(harmony.Id);active=null;}
    #endregion
    #region Private
    /// <summary>Snapshots the shared state owner after typed submission publishes its resources.</summary>
    private static void Observe()
    {
        if(active is null)return;
        var bindings=ReadBindings();active.Draws.Add((StateCache.Current.GetCurrentFramebuffer(FramebufferTarget.DrawFramebuffer),bindings.Current,bindings.History));
        if(active.Draws.Count==active.FailAtDraw)throw new InvalidOperationException("Injected cloud submission failure.");
    }
    #endregion
}
