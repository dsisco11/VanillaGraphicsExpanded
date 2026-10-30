using OpenTK;
using OpenTK.Graphics.OpenGL;
using System.Runtime.InteropServices;
using GLFWBindingsContext = OpenTK.Windowing.GraphicsLibraryFramework.GLFWBindingsContext;

namespace VanillaGraphicsExpanded.Tests.GPU.Fixtures;

/// <summary>Counts original driver submissions and can simulate one attributed native routing error.</summary>
internal sealed unsafe class GraphicsTransitionCapture : IBindingsContext, IDisposable
{
    [ThreadStatic] private static GraphicsTransitionCapture? active;
    private static readonly List<Delegate> roots = [];
    private readonly GLFWBindingsContext driver = new();
    private readonly Dictionary<string, nint> pointers = [];
    internal readonly List<string> Calls = [];
    /// <summary>Selects a target whose second named routing submission reports a simulated native error.</summary>
    internal int FailNamedFramebuffer = -1;
    private int failureTargetCalls;
    private bool pendingRoutingError;

    #region Public API
    /// <summary>Counts actual selected-target submissions, distinguishing authored routing from restoration.</summary>
    internal int SelectedTargetRoutingCalls => failureTargetCalls;
    /// <summary>Counts simulated error statuses actually delivered to managed error attribution.</summary>
    internal int InjectedRoutingErrors { get; private set; }
    /// <summary>Counts genuine native errors independently of the simulated status.</summary>
    internal int DriverErrorCount { get; private set; }

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
                ((delegate* unmanaged[Stdcall]<uint, int, void>)named)(id, buffer);
                RecordNamed(id, ((DrawBufferMode)buffer).ToString());
            }));
            nint nameds = driver.GetProcAddress("glNamedFramebufferDrawBuffers");
            Add("glNamedFramebufferDrawBuffers", new Nameds((id, count, buffers) =>
            {
                ((delegate* unmanaged[Stdcall]<uint, int, int*, void>)nameds)(id, count, buffers);
                RecordNamed(id, Route(count, buffers));
            }));
            nint errors = driver.GetProcAddress("glGetError");
            Add("glGetError", new ErrorQuery(() =>
            {
                uint error = ((delegate* unmanaged[Stdcall]<uint>)errors)();
                // Preserve every real error and forward the real query once. Only a clean
                // status may carry our single simulated failure after actual restoration.
                bool observing = ReferenceEquals(active, this);
                if (error != (uint)ErrorCode.NoError)
                {
                    if (observing) DriverErrorCount++;
                    return error;
                }
                if (observing && pendingRoutingError)
                {
                    pendingRoutingError = false;
                    FailNamedFramebuffer = -1;
                    InjectedRoutingErrors++;
                    return (uint)ErrorCode.InvalidOperation;
                }
                return error;
            }));
            GL.LoadBindings(this);
            OpenTK.Graphics.OpenGL4.GL.LoadBindings(this);
        }
        catch { Dispose(); throw; }
    }

    /// <inheritdoc />
    public nint GetProcAddress(string name) => pointers.TryGetValue(name, out var pointer) ? pointer : driver.GetProcAddress(name);

    /// <summary>Disables simulated errors before restoring driver loading; retained callbacks cannot inject errors.</summary>
    public void Dispose()
    {
        pendingRoutingError = false;
        active = null;
        GL.LoadBindings(driver);
        OpenTK.Graphics.OpenGL4.GL.LoadBindings(driver);
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

    /// <summary>Records named submissions and arms an error only after the selected target's restoration submission.</summary>
    private void RecordNamed(uint id, string route)
    {
        Calls.Add($"RoutingNamed:{id}:{route}");
        if (ReferenceEquals(active, this) && FailNamedFramebuffer == (int)id)
        {
            failureTargetCalls++;
            if (failureTargetCalls == 2) pendingRoutingError = true;
        }
    }

    /// <summary>Matches the GLenum result ABI of a native error-status query.</summary>
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate uint ErrorQuery();
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
