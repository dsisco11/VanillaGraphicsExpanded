using HarmonyLib;
using OpenTK.Graphics.OpenGL;
using GL4 = OpenTK.Graphics.OpenGL4.GL;
using VanillaGraphicsExpanded.Rendering;

namespace VanillaGraphicsExpanded.Tests.GPU.Fixtures;

/// <summary>Counts routing validation work independently of native calls for immutable pass qualification.</summary>
internal sealed class FramebufferRoutingWorkCapture : IDisposable
{
    [ThreadStatic] private static FramebufferRoutingWorkCapture? active;
    private readonly Harmony harmony = new("Tests.FramebufferRoutingWorkCapture");
    internal int DynamicApplications, Captures, CompletenessChecks;
    #region Public API
    /// <summary>Installs observers after framebuffer and pass declaration construction have finished.</summary>
    internal FramebufferRoutingWorkCapture()
    {
        if (active is not null) throw new InvalidOperationException("Routing work capture already active.");
        active = this;
        try
        {
            // Observe the final backend's native work, including its OpenGL4 entry points.
            Patch(typeof(GL4),nameof(GL4.NamedFramebufferDrawBuffers),[typeof(int),typeof(int),typeof(OpenTK.Graphics.OpenGL4.DrawBuffersEnum[])],nameof(Dynamic));
            Patch(typeof(GL4),nameof(GL4.NamedFramebufferDrawBuffer),[typeof(int),typeof(OpenTK.Graphics.OpenGL4.DrawBufferMode)],nameof(Dynamic));
            Patch(typeof(GL4),nameof(GL4.DrawBuffers),[typeof(int),typeof(OpenTK.Graphics.OpenGL4.DrawBuffersEnum[])],nameof(Dynamic));
            Patch(typeof(GL4),nameof(GL4.DrawBuffer),[typeof(OpenTK.Graphics.OpenGL4.DrawBufferMode)],nameof(Dynamic));
            Patch(typeof(GL4),nameof(GL4.GetInteger),[typeof(OpenTK.Graphics.OpenGL4.GetPName),typeof(int).MakeByRefType()],nameof(Capture));
            Patch(typeof(GL4),nameof(GL4.CheckNamedFramebufferStatus),[typeof(int),typeof(OpenTK.Graphics.OpenGL4.FramebufferTarget)],nameof(Completeness));
            Patch(typeof(GL),nameof(GL.CheckFramebufferStatus),[typeof(FramebufferTarget)],nameof(Completeness));
            Patch(typeof(GL4),nameof(GL4.CheckFramebufferStatus),[typeof(OpenTK.Graphics.OpenGL4.FramebufferTarget)],nameof(Completeness));
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
    /// <summary>Observes an exact native overload without depending on retired execution owners.</summary>
    private void Patch(Type owner,string method,Type[] arguments,string observer)
        => harmony.Patch(AccessTools.Method(owner,method,arguments) ?? throw new MissingMethodException(owner.FullName,method),
            prefix:new HarmonyMethod(AccessTools.Method(typeof(FramebufferRoutingWorkCapture),observer)));
    /// <summary>Counts actual native routing writes.</summary>
    private static void Dynamic() { if (active is not null) active.DynamicApplications++; }
    /// <summary>Counts live dynamic routing snapshot requests.</summary>
    private static void Capture(OpenTK.Graphics.OpenGL4.GetPName __0)
    {
        if (active is not null && __0 >= OpenTK.Graphics.OpenGL4.GetPName.DrawBuffer0 &&
            __0 < OpenTK.Graphics.OpenGL4.GetPName.DrawBuffer0 + GpuSupport.Graphics.MaxDrawBuffers) active.Captures++;
    }
    /// <summary>Counts actual native completeness checks rather than managed no-op dispatch.</summary>
    private static void Completeness() { if (active is not null) active.CompletenessChecks++; }
    #endregion
}
