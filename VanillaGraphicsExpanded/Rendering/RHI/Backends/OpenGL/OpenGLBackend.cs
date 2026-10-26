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
    /// <summary>Retains the original capability snapshot for backend-local policy and explicit local tools.</summary>
    internal GraphicsCapabilities NativeCapabilities { get; }

    /// <summary>Reports registered origin retirement without issuing GL or accepting another context.</summary>
    internal bool HasRetiredOrigin => origin != default && !RenderContextRegistry.IsAlive(origin);

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
        foreach (string name in RequiredEntryPoints)
            if (bindings.GetProcAddress(name) == IntPtr.Zero) throw new NotSupportedException($"Required OpenGL entry point is unavailable: {name}.");
        NativeCapabilities = capabilities;
        Capabilities = new(capabilities);
    }
    #endregion

    #region Private
    private static readonly string[] RequiredEntryPoints =
    [ "glFenceSync", "glClientWaitSync", "glWaitSync", "glDeleteSync", "glIsSync", "glMemoryBarrier", "glFlush", "glFinish",
      "glCreateQueries", "glBeginQuery", "glEndQuery", "glQueryCounter", "glGetQueryObjectiv", "glGetQueryObjectui64v", "glDeleteQueries", "glIsQuery",
      "glBeginConditionalRender", "glEndConditionalRender", "glBindTransformFeedback", "glDeleteTransformFeedbacks", "glIsTransformFeedback",
      "glCreateTransformFeedbacks", "glTransformFeedbackBufferRange", "glBeginTransformFeedback", "glEndTransformFeedback", "glPauseTransformFeedback", "glResumeTransformFeedback",
      "glCreateBuffers", "glNamedBufferData", "glNamedBufferStorage", "glNamedBufferSubData",
      "glCopyNamedBufferSubData", "glClearNamedBufferSubData", "glMapNamedBufferRange",
      "glFlushMappedNamedBufferRange", "glUnmapNamedBuffer", "glGetNamedBufferParameteri64v",
      "glGetNamedBufferParameteriv", "glGetNamedBufferSubData", "glDeleteBuffers", "glIsBuffer",
      "glBindBuffer", "glBindBufferBase", "glBindBufferRange", "glCreateTextures", "glTextureStorage1D", "glTextureStorage2D", "glTextureStorage3D",
      "glTextureStorage2DMultisample", "glTextureStorage3DMultisample", "glTextureSubImage1D", "glTextureSubImage2D", "glTextureSubImage3D", "glCopyImageSubData",
      "glClearTexSubImage", "glGenerateTextureMipmap", "glTextureView", "glTextureBuffer", "glTextureBufferRange", "glTextureParameteri", "glGetTextureParameteriv",
      "glGetTextureLevelParameteriv", "glGetTextureSubImage", "glCreateRenderbuffers", "glNamedRenderbufferStorage", "glNamedRenderbufferStorageMultisample",
      "glGetNamedRenderbufferParameteriv", "glCreateSamplers", "glSamplerParameteri", "glSamplerParameterf", "glSamplerParameterfv",
      "glTexImage1D", "glTexImage2D", "glTexImage3D", "glTexImage2DMultisample", "glTexImage3DMultisample",
      "glActiveTexture", "glBindTexture", "glBindSampler", "glBindRenderbuffer", "glPixelStorei", "glGetIntegerv",
      "glGetTexLevelParameteriv", "glDeleteTextures", "glDeleteRenderbuffers", "glDeleteSamplers" ];
    #endregion
}
