using HarmonyLib;
using VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;

namespace VanillaGraphicsExpanded.Tests.GPU.Fixtures;

/// <summary>Observes or interrupts the real backend fullscreen draw without substituting engine geometry.</summary>
internal sealed class NativeDrawObserver : IDisposable
{
    [ThreadStatic] private static NativeDrawObserver? active;
    private readonly Harmony harmony = new("Tests.NativeDrawObserver");
    private readonly Action beforeDraw;

    #region Internal API
    /// <summary>Installs one backend dispatch-boundary observer for the current test interval.</summary>
    internal NativeDrawObserver(Action beforeDraw)
    {
        Assert.Null(active);
        this.beforeDraw = beforeDraw;
        active = this;
        try
        {
            // Native GL wrappers may already be inlined. Observe the nontrivial packet preparation
            // immediately before dispatch, with the backend's resource guards already held.
            harmony.Patch(AccessTools.Method(typeof(OpenGLBackend), "PrepareGraphicsPacket"),
                postfix: new HarmonyMethod(typeof(NativeDrawObserver), nameof(Observe)));
        }
        catch { Dispose(); throw; }
    }
    #endregion
    #region Public API
    /// <summary>Removes the observer before fixture resources are retired.</summary>
    public void Dispose() { harmony.UnpatchAll(harmony.Id); active = null; }
    #endregion
    #region Private
    /// <summary>Runs the authored observation while the backend's resource and publication guards are held.</summary>
    private static void Observe() => active?.beforeDraw();
    #endregion
}
