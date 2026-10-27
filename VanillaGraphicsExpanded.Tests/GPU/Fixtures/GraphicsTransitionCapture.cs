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
    private int failureTargetCalls;
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
            Delegate[] core = [
                (Action<OpenTK.Graphics.OpenGL4.FramebufferTarget,int>)OpenTK.Graphics.OpenGL4.GL.BindFramebuffer,
                (Action<int>)OpenTK.Graphics.OpenGL4.GL.UseProgram,
                (Action<int,OpenTK.Graphics.OpenGL4.DrawBuffersEnum[]>)OpenTK.Graphics.OpenGL4.GL.DrawBuffers,
                (Action<int,int,OpenTK.Graphics.OpenGL4.DrawBuffersEnum[]>)OpenTK.Graphics.OpenGL4.GL.NamedFramebufferDrawBuffers,
                (Action<OpenTK.Graphics.OpenGL4.DrawBufferMode>)OpenTK.Graphics.OpenGL4.GL.DrawBuffer,
                (Action<int,OpenTK.Graphics.OpenGL4.DrawBufferMode>)OpenTK.Graphics.OpenGL4.GL.NamedFramebufferDrawBuffer
            ];
            string[] observers = [nameof(Framebuffer),nameof(Program),nameof(Routing),nameof(NamedRouting),nameof(WindowRouting),nameof(NamedRouting)];
            for (int index=0;index<core.Length;index++)
                harmony.Patch(core[index].Method,prefix:new HarmonyMethod(typeof(GraphicsTransitionCapture),observers[index]));
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
    private static void Framebuffer(object[] __args)=>active?.Calls.Add($"Framebuffer:{__args[0]}:{__args[1]}");
    /// <summary>Records actual requested program transitions, including handback to the native owner.</summary>
    private static void Program(int __0)=>active?.Calls.Add($"Program:{__0}");
    /// <summary>Records framebuffer-local output routing changes independently of framebuffer bindings.</summary>
    private static void Routing(object[] __args)=>active?.Calls.Add($"Routing:{GL.GetInteger(GetPName.DrawFramebufferBinding)}:{string.Join(',',((Array)__args[1]).Cast<object>().Take((int)__args[0]))}");
    /// <summary>Records direct routing restoration and optionally injects a single independent-owner cleanup failure.</summary>
    private static void NamedRouting(object[] __args)
    {
        int __0=(int)__args[0], __1=__args.Length==3?(int)__args[1]:1;
        var __2=__args.Length==3?((Array)__args[2]).Cast<object>():new[] { __args[1] };
        active?.Calls.Add($"RoutingNamed:{__0}:{string.Join(',',__2.Take(__1))}");
        if(active?.FailNamedFramebuffer==__0 && ++active.failureTargetCalls == 2)
        {
            active.FailNamedFramebuffer=-1;throw new InvalidOperationException("Injected named restoration failure.");
        }
    }
    /// <summary>Records default-window routing without treating it as named framebuffer state.</summary>
    private static void WindowRouting(object[] __args)=>active?.Calls.Add($"RoutingWindow:{GL.GetInteger(GetPName.DrawFramebufferBinding)}:{__args[0]}");
    #endregion
    #endregion
}
