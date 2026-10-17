using HarmonyLib;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.Rendering;

namespace VanillaGraphicsExpanded.Tests.GPU.Fixtures;

/// <summary>Counts routing validation work independently of native calls for immutable pass qualification.</summary>
internal sealed class FramebufferRoutingWorkCapture : IDisposable
{
    [ThreadStatic] private static FramebufferRoutingWorkCapture? active;
    private readonly Harmony harmony = new("Tests.FramebufferRoutingWorkCapture");
    internal int DynamicApplications, Captures, StaticChecks, CompletenessChecks;
    #region Public API
    /// <summary>Installs observers after framebuffer and pass declaration construction have finished.</summary>
    internal FramebufferRoutingWorkCapture()
    {
        if (active is not null) throw new InvalidOperationException("Routing work capture already active.");
        active = this;
        try
        {
            harmony.Patch(AccessTools.Method(typeof(StateCache), "ApplyFramebufferDrawRouting"), prefix: new HarmonyMethod(AccessTools.Method(typeof(FramebufferRoutingWorkCapture), nameof(Dynamic))));
            harmony.Patch(AccessTools.Method(typeof(StateCache), "CaptureFramebufferDrawRouting"), prefix: new HarmonyMethod(AccessTools.Method(typeof(FramebufferRoutingWorkCapture), nameof(Capture))));
            harmony.Patch(AccessTools.Method(typeof(StaticGpuFramebuffer), "ValidatePassRouting"), prefix: new HarmonyMethod(AccessTools.Method(typeof(FramebufferRoutingWorkCapture), nameof(Static))));
            harmony.Patch(AccessTools.Method(typeof(GL), nameof(GL.CheckFramebufferStatus), [typeof(FramebufferTarget)]), prefix: new HarmonyMethod(AccessTools.Method(typeof(FramebufferRoutingWorkCapture), nameof(Completeness))));
        }
        catch { Dispose(); throw; }
    }
    /// <summary>Removes only this fixture's observers after normal or exceptional completion.</summary>
    public void Dispose()
    {
        harmony.UnpatchAll(harmony.Id);
        active = null;
    }
    #endregion
    #region Private
    /// <summary>Counts calls that normalize and compare dynamic routing.</summary>
    private static void Dynamic() { if (active is not null) active.DynamicApplications++; }
    /// <summary>Counts live dynamic routing snapshot requests.</summary>
    private static void Capture() { if (active is not null) active.Captures++; }
    /// <summary>Counts immutable declaration checks that must occur before repeated submission.</summary>
    private static void Static() { if (active is not null) active.StaticChecks++; }
    /// <summary>Counts actual native completeness checks rather than managed no-op dispatch.</summary>
    private static void Completeness() { if (active is not null) active.CompletenessChecks++; }
    #endregion
}
