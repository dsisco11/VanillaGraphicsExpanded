using HarmonyLib;
using OpenTK;
using OpenTK.Graphics.OpenGL;
using System.Runtime.InteropServices;
using VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;
using GLFWBindingsContext = OpenTK.Windowing.GraphicsLibraryFramework.GLFWBindingsContext;

namespace VanillaGraphicsExpanded.Tests.GPU.Fixtures;

/// <summary>Counts driver submissions from both OpenTK and backend-owned original-pointer dispatch.</summary>
internal sealed unsafe class GraphicsTransitionCapture : IBindingsContext, IDisposable
{
    [ThreadStatic] private static GraphicsTransitionCapture? active;
    private static readonly List<Delegate> roots = [];
    private readonly Harmony harmony = new("Tests.GraphicsTransitionCapture");
    private readonly GLFWBindingsContext driver = new();
    private readonly Dictionary<string, nint> pointers = [];
    internal readonly List<string> Calls = [];
    /// <summary>Selects a managed restoration failure without throwing across an unmanaged callback.</summary>
    internal int FailNamedFramebuffer = -1;
    private int failureTargetCalls;

    #region Public API
    /// <summary>Decorates real driver addresses before production interception installs its callbacks.</summary>
    internal GraphicsTransitionCapture()
    {
        if (active is not null) throw new InvalidOperationException("Graphics transition capture already active.");
        active = this;
        try
        {
            nint bind = driver.GetProcAddress("glBindFramebuffer");
            Add("glBindFramebuffer", new Bind((target, id) =>
            {
                Calls.Add($"Framebuffer:{(FramebufferTarget)target}:{id}");
                ((delegate* unmanaged[Stdcall]<int, uint, void>)bind)(target, id);
            }));
            nint use = driver.GetProcAddress("glUseProgram");
            Add("glUseProgram", new Use(id =>
            {
                Calls.Add($"Program:{id}");
                ((delegate* unmanaged[Stdcall]<uint, void>)use)(id);
            }));
            nint draw = driver.GetProcAddress("glDrawBuffer");
            Add("glDrawBuffer", new Draw(buffer =>
            {
                Calls.Add($"RoutingWindow:{GL.GetInteger(GetPName.DrawFramebufferBinding)}:{(DrawBufferMode)buffer}");
                ((delegate* unmanaged[Stdcall]<int, void>)draw)(buffer);
            }));
            nint draws = driver.GetProcAddress("glDrawBuffers");
            Add("glDrawBuffers", new Draws((count, buffers) =>
            {
                Calls.Add($"Routing:{GL.GetInteger(GetPName.DrawFramebufferBinding)}:{Route(count, buffers)}");
                ((delegate* unmanaged[Stdcall]<int, int*, void>)draws)(count, buffers);
            }));
            nint named = driver.GetProcAddress("glNamedFramebufferDrawBuffer");
            Add("glNamedFramebufferDrawBuffer", new Named((id, buffer) =>
            {
                RecordNamed(id, ((DrawBufferMode)buffer).ToString());
                ((delegate* unmanaged[Stdcall]<uint, int, void>)named)(id, buffer);
            }));
            nint nameds = driver.GetProcAddress("glNamedFramebufferDrawBuffers");
            Add("glNamedFramebufferDrawBuffers", new Nameds((id, count, buffers) =>
            {
                RecordNamed(id, Route(count, buffers));
                ((delegate* unmanaged[Stdcall]<uint, int, int*, void>)nameds)(id, count, buffers);
            }));
            harmony.Patch(AccessTools.Method(typeof(OpenGLFramebufferRouting), "Restore"),
                prefix: new HarmonyMethod(typeof(GraphicsTransitionCapture), nameof(BeforeRestore)));
            GL.LoadBindings(this);
            OpenTK.Graphics.OpenGL4.GL.LoadBindings(this);
        }
        catch { Dispose(); throw; }
    }

    /// <inheritdoc />
    public nint GetProcAddress(string name) => pointers.TryGetValue(name, out var pointer) ? pointer : driver.GetProcAddress(name);

    /// <summary>Restores ordinary driver loading and removes only this fixture's managed failure injection.</summary>
    public void Dispose()
    {
        GL.LoadBindings(driver);
        OpenTK.Graphics.OpenGL4.GL.LoadBindings(driver);
        harmony.UnpatchAll(harmony.Id);
        active = null;
    }
    #endregion

    #region Private
    /// <summary>Roots callbacks for the lifetime of production retained native slots.</summary>
    private void Add(string name, Delegate callback)
    {
        roots.Add(callback);
        pointers.Add(name, Marshal.GetFunctionPointerForDelegate(callback));
    }

    /// <summary>Copies native routing only for the duration of the synchronous callback.</summary>
    private static string Route(int count, int* values)
    {
        var result = new string[Math.Max(0, count)];
        for (int i = 0; i < result.Length; i++) result[i] = ((DrawBuffersEnum)values[i]).ToString();
        return string.Join(',', result);
    }

    /// <summary>Records named submissions independently from selected framebuffer state.</summary>
    private void RecordNamed(uint id, string route)
    {
        Calls.Add($"RoutingNamed:{id}:{route}");
        if (FailNamedFramebuffer == (int)id) failureTargetCalls++;
    }

    /// <summary>Injects cleanup failure in managed code after the target's first authored submission.</summary>
    private static void BeforeRestore(int framebuffer)
    {
        if (active is { } capture && capture.FailNamedFramebuffer == framebuffer && capture.failureTargetCalls > 0)
        {
            capture.FailNamedFramebuffer = -1;
            throw new InvalidOperationException("Injected named restoration failure.");
        }
    }

    /// <summary>Matches framebuffer selection ABI.</summary>
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate void Bind(int target, uint id);
    /// <summary>Matches program selection ABI.</summary>
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate void Use(uint id);
    /// <summary>Matches scalar draw routing ABI.</summary>
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate void Draw(int buffer);
    /// <summary>Matches array draw routing ABI.</summary>
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate void Draws(int count, int* buffers);
    /// <summary>Matches named scalar routing ABI.</summary>
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate void Named(uint id, int buffer);
    /// <summary>Matches named array routing ABI.</summary>
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate void Nameds(uint id, int count, int* buffers);
    #endregion
}
