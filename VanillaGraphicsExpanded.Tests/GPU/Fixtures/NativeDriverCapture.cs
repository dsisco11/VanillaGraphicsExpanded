using OpenTK;
using System.Runtime.InteropServices;
using GLFWBindingsContext = OpenTK.Windowing.GraphicsLibraryFramework.GLFWBindingsContext;

namespace VanillaGraphicsExpanded.Tests.GPU.Fixtures;

/// <summary>Decorates real native addresses so assertions observe both OpenTK calls and saved-original backend dispatch.</summary>
internal sealed class NativeDriverCapture : IBindingsContext, IDisposable
{
    private static readonly List<Delegate> roots = [];
    private readonly GLFWBindingsContext driver = new();
    private readonly Dictionary<string, nint> replacements = [];
    private Exception? failure;
    private bool active;

    #region Internal API
    /// <summary>Obtains the actual driver address before installing the test decorator.</summary>
    internal nint Original(string name) => driver.GetProcAddress(name);

    /// <summary>Roots a typed ABI callback for previously fetched production callbacks as well as the current loader.</summary>
    internal void Add(string name, Delegate callback)
    {
        roots.Add(callback);
        replacements.Add(name, Marshal.GetFunctionPointerForDelegate(callback));
    }

    /// <summary>Installs decorators in both binding roots through the production loader interception boundary.</summary>
    internal void Install()
    {
        active = true;
        OpenTK.Graphics.OpenGL.GL.LoadBindings(this);
        OpenTK.Graphics.OpenGL4.GL.LoadBindings(this);
    }

    /// <summary>Contains assertion or injected callback failures instead of throwing through unmanaged dispatch.</summary>
    internal void Observe(Action action)
    {
        if (!active) return;
        try { action(); }
        catch (Exception error) { failure ??= error; }
    }

    /// <summary>Reports a captured observer fault from the managed test boundary.</summary>
    internal void RequireSuccessfulObservation()
    {
        if (failure is not null) throw new InvalidOperationException("Native test observation failed.", failure);
    }
    #endregion

    #region Public API
    /// <inheritdoc />
    public nint GetProcAddress(string name) => replacements.TryGetValue(name, out var pointer)
        ? pointer : driver.GetProcAddress(name);

    /// <summary>Restores ordinary driver loading while retained callback addresses remain callable.</summary>
    public void Dispose()
    {
        active = false;
        OpenTK.Graphics.OpenGL.GL.LoadBindings(driver);
        OpenTK.Graphics.OpenGL4.GL.LoadBindings(driver);
        RequireSuccessfulObservation();
    }
    #endregion
}
