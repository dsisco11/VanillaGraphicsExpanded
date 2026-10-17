using HarmonyLib;
using OpenTK.Graphics.OpenGL;

namespace VanillaGraphicsExpanded.Tests.GPU.Fixtures;

/// <summary>Counts managed OpenGL entry calls while leaving native submission unchanged.</summary>
internal sealed class GraphicsTransitionCapture:IDisposable
{
    [ThreadStatic] private static GraphicsTransitionCapture? active;
    private readonly Harmony harmony=new("Tests.GraphicsTransitionCapture");
    internal readonly List<string> Calls=[];
    /// <summary>Selects one named restore to reject, leaving subsequent owners available for independent cleanup.</summary>
    internal int FailNamedFramebuffer=-1;
    #region Public API
    /// <summary>Installs observers for the integer/array overloads used by the production state cache and passes.</summary>
    internal GraphicsTransitionCapture()
    {
        if(active is not null)throw new InvalidOperationException("Graphics transition capture already active.");
        active=this;
        try
        {
            Patch(nameof(GL.BindFramebuffer),[typeof(FramebufferTarget),typeof(int)],nameof(Framebuffer));
            Patch(nameof(GL.UseProgram),[typeof(int)],nameof(Program));
            Patch(nameof(GL.DrawBuffers),[typeof(int),typeof(DrawBuffersEnum[])],nameof(Routing));
            Patch(nameof(GL.NamedFramebufferDrawBuffers),[typeof(int),typeof(int),typeof(DrawBuffersEnum[])],nameof(NamedRouting));
            Patch(nameof(GL.DrawBuffer),[typeof(DrawBufferMode)],nameof(WindowRouting));
        }
        catch { Dispose();throw; }
    }
    /// <summary>Removes only this fixture's observers, including after assertion failures.</summary>
    public void Dispose(){harmony.UnpatchAll(harmony.Id);active=null;}
    #endregion
    #region Private
    #region Registration
    /// <summary>Resolves an exact public overload so forwarding overloads are never double counted.</summary>
    private void Patch(string name,Type[] arguments,string observer)
    {
        var method=AccessTools.Method(typeof(GL),name,arguments)??throw new MissingMethodException(name);
        harmony.Patch(method,prefix:new HarmonyMethod(AccessTools.Method(typeof(GraphicsTransitionCapture),observer)));
    }
    #endregion
    #region Observation
    /// <summary>Records framebuffer binding category and object id before native execution.</summary>
    private static void Framebuffer(FramebufferTarget __0,int __1)=>active?.Calls.Add($"Framebuffer:{__0}:{__1}");
    /// <summary>Records actual requested program transitions, including handback to the native owner.</summary>
    private static void Program(int __0)=>active?.Calls.Add($"Program:{__0}");
    /// <summary>Records framebuffer-local output routing changes independently of framebuffer bindings.</summary>
    private static void Routing(int __0,DrawBuffersEnum[] __1)=>active?.Calls.Add($"Routing:{GL.GetInteger(GetPName.DrawFramebufferBinding)}:{string.Join(',',__1.Take(__0))}");
    /// <summary>Records direct routing restoration and optionally injects a single independent-owner cleanup failure.</summary>
    private static void NamedRouting(int __0,int __1,DrawBuffersEnum[] __2)
    {
        active?.Calls.Add($"RoutingNamed:{__0}:{string.Join(',',__2.Take(__1))}");
        if(active?.FailNamedFramebuffer==__0)
        {
            active.FailNamedFramebuffer=-1;throw new InvalidOperationException("Injected named restoration failure.");
        }
    }
    /// <summary>Records default-window routing without treating it as named framebuffer state.</summary>
    private static void WindowRouting(DrawBufferMode __0)=>active?.Calls.Add($"RoutingWindow:{GL.GetInteger(GetPName.DrawFramebufferBinding)}:{__0}");
    #endregion
    #endregion
}
