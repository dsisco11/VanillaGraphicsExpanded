using System.Runtime.InteropServices;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.Rendering;

namespace VanillaGraphicsExpanded.Tests.GPU.Fixtures;

/// <summary>Counts actual native routing work through external and saved-original backend dispatch.</summary>
internal sealed unsafe class FramebufferRoutingWorkCapture : IDisposable
{
    private readonly NativeDriverCapture native = new();

    /// <summary>Matches a single bound routing selection.</summary>
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate void Selection(int mode);
    /// <summary>Matches a single named routing selection.</summary>
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate void NamedSelection(uint name, int mode);
    /// <summary>Matches multiple bound routing selections.</summary>
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate void Selections(int count, int* modes);
    /// <summary>Matches multiple named routing selections.</summary>
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate void NamedSelections(uint name, int count, int* modes);
    /// <summary>Matches integer state output.</summary>
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate void IntegerOutput(int parameter, int* value);
    /// <summary>Matches a bound framebuffer completeness result.</summary>
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int Status(int target);
    /// <summary>Matches a named framebuffer completeness result.</summary>
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int NamedStatus(uint name, int target);

    #region Internal API
    internal int DynamicApplications, Captures, CompletenessChecks;

    /// <summary>Decorates the final native slots after pass declarations have been constructed.</summary>
    internal FramebufferRoutingWorkCapture()
    {
        var single = (delegate* unmanaged[Stdcall]<int, void>)native.Original("glDrawBuffer");
        native.Add("glDrawBuffer", new Selection(mode => { single(mode); DynamicApplications++; }));
        var named = (delegate* unmanaged[Stdcall]<uint, int, void>)native.Original("glNamedFramebufferDrawBuffer");
        native.Add("glNamedFramebufferDrawBuffer", new NamedSelection((name, mode) => { named(name, mode); DynamicApplications++; }));
        var multiple = (delegate* unmanaged[Stdcall]<int, int*, void>)native.Original("glDrawBuffers");
        native.Add("glDrawBuffers", new Selections((count, modes) => { multiple(count, modes); DynamicApplications++; }));
        var namedMultiple = (delegate* unmanaged[Stdcall]<uint, int, int*, void>)native.Original("glNamedFramebufferDrawBuffers");
        native.Add("glNamedFramebufferDrawBuffers", new NamedSelections((name, count, modes) => { namedMultiple(name, count, modes); DynamicApplications++; }));
        var get = (delegate* unmanaged[Stdcall]<int, int*, void>)native.Original("glGetIntegerv");
        int maxDrawBuffers = GpuSupport.Graphics.MaxDrawBuffers;
        native.Add("glGetIntegerv", new IntegerOutput((parameter, value) =>
        {
            get(parameter, value);
            if (parameter >= (int)GetPName.DrawBuffer0 && parameter < (int)GetPName.DrawBuffer0 + maxDrawBuffers) Captures++;
        }));
        var status = (delegate* unmanaged[Stdcall]<int, int>)native.Original("glCheckFramebufferStatus");
        native.Add("glCheckFramebufferStatus", new Status(target => { int result = status(target); CompletenessChecks++; return result; }));
        var namedStatus = (delegate* unmanaged[Stdcall]<uint, int, int>)native.Original("glCheckNamedFramebufferStatus");
        native.Add("glCheckNamedFramebufferStatus", new NamedStatus((name, target) => { int result = namedStatus(name, target); CompletenessChecks++; return result; }));
        native.Install();
    }
    #endregion

    #region Public API
    /// <summary>Restores ordinary native dispatch after the measured interval.</summary>
    public void Dispose() => native.Dispose();
    #endregion
}
