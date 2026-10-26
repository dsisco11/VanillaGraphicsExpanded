using System;
using OpenTK.Windowing.GraphicsLibraryFramework;
using VanillaGraphicsExpanded.Rendering.Integration;
using GL = OpenTK.Graphics.OpenGL4.GL;

namespace VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;

/// <summary>Owns native execution for one admitted context and its existing state cache.</summary>
internal sealed partial class OpenGLBackend : IRHIBackend
{
    private readonly StateCache cache;
    private readonly nint nativeContext;
    private readonly (nint Handle, long Generation) origin;
    private readonly int thread = Environment.CurrentManagedThreadId;

    #region Public API
    /// <inheritdoc />
    public RHICapabilities Capabilities { get; }
    /// <inheritdoc />
    public void RequireContext()
    {
        if (Environment.CurrentManagedThreadId != thread || !IsCurrentContext)
            throw new InvalidOperationException("The original RHI context and thread are required.");
        cache.SelectRHIContext(nativeContext, origin);
    }
    #endregion

    #region Internal API
    /// <summary>Reports exact context identity, without accepting a replacement registered lifetime.</summary>
    internal unsafe bool IsCurrentContext => nativeContext == (nint)GLFW.GetCurrentContext()
        && origin == RenderContextRegistry.Current() && (origin == default || RenderContextRegistry.IsAlive(origin));

    /// <summary>Loads core bindings and admits the required baseline once, before any managed execution.</summary>
    internal unsafe OpenGLBackend(StateCache cache)
    {
        nativeContext = (nint)GLFW.GetCurrentContext();
        if (nativeContext == 0) throw new InvalidOperationException("A current native context is required for RHI admission.");
        origin = RenderContextRegistry.Current();
        this.cache = cache;
        var bindings = new GLFWBindingsContext();
        GL.LoadBindings(bindings);
        // Discovery remains with GpuSupport; admission only checks the already captured API version.
        var capabilities = GpuSupport.CaptureRHIContext();
        if (capabilities.IsOpenGles || capabilities.ApiVersion is not { } version || version < new Version(4, 5))
            throw new NotSupportedException("The RHI requires desktop OpenGL 4.5 or newer.");
        foreach (string name in RequiredBufferEntryPoints)
            if (bindings.GetProcAddress(name) == IntPtr.Zero) throw new NotSupportedException($"Required OpenGL entry point is unavailable: {name}.");
        Capabilities = new(capabilities);
    }
    #endregion

    #region Private
    private static readonly string[] RequiredBufferEntryPoints =
    [ "glCreateBuffers", "glNamedBufferData", "glNamedBufferStorage", "glNamedBufferSubData",
      "glCopyNamedBufferSubData", "glClearNamedBufferSubData", "glMapNamedBufferRange",
      "glFlushMappedNamedBufferRange", "glUnmapNamedBuffer", "glGetNamedBufferParameteri64v",
      "glGetNamedBufferParameteriv", "glGetNamedBufferSubData", "glDeleteBuffers", "glIsBuffer",
      "glBindBuffer", "glBindBufferBase", "glBindBufferRange" ];
    #endregion
}
