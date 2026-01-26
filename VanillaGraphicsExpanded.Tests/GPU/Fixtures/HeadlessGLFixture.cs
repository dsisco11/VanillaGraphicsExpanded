using System;
using System.Threading.Tasks;
using OpenTK.Graphics.OpenGL;
using OpenTK.Mathematics;
using OpenTK.Windowing.Common;
using OpenTK.Windowing.Desktop;
using OpenTK.Windowing.GraphicsLibraryFramework;
using Xunit;

namespace VanillaGraphicsExpanded.Tests.GPU.Fixtures;

/// <summary>
/// xUnit fixture that creates a headless OpenGL 4.3 context for GPU tests.
/// Uses OpenTK.Windowing.Desktop to create a hidden window with an OpenGL context.
/// This uses the same OpenTK libraries as the production code, ensuring full compatibility.
/// </summary>
/// <remarks>
/// Usage:
/// <code>
/// [Collection("GPU")]
/// [Trait("Category", "GPU")]
/// public class MyGpuTests : IClassFixture&lt;HeadlessGLFixture&gt;
/// {
///     private readonly HeadlessGLFixture _fixture;
///     
///     public MyGpuTests(HeadlessGLFixture fixture)
///     {
///         _fixture = fixture;
///     }
/// }
/// </code>
/// </remarks>
public sealed class HeadlessGLFixture : IAsyncLifetime
{
    private unsafe Window* _glfwWindow;
    private bool _contextValid;
    private string? _initializationError;
    private int _bindingsLoadedThreadId = -1;

    /// <summary>
    /// Whether the OpenGL context was successfully created and is valid.
    /// </summary>
    public bool IsContextValid => _contextValid;

    /// <summary>
    /// Error message if context creation failed, null otherwise.
    /// </summary>
    public string? InitializationError => _initializationError;

    /// <summary>
    /// OpenGL version string (e.g., "4.6.0 NVIDIA 546.33").
    /// </summary>
    public string? GLVersion { get; private set; }

    /// <summary>
    /// OpenGL renderer string (e.g., "NVIDIA GeForce RTX 3080").
    /// </summary>
    public string? GLRenderer { get; private set; }

    /// <summary>
    /// Ensures the GL context is valid; throws skip exception if not.
    /// Call this at the start of tests that require GPU.
    /// </summary>
    public void EnsureContextValid()
    {
        Assert.SkipWhen(!_contextValid, $"OpenGL context not available: {_initializationError ?? "Unknown error"}");

        unsafe
        {
            if (_glfwWindow != null && GLFW.GetCurrentContext() != _glfwWindow)
            {
                GLFW.MakeContextCurrent(_glfwWindow);
            }
        }

        int threadId = Environment.CurrentManagedThreadId;
        if (threadId != _bindingsLoadedThreadId)
        {
            GL.LoadBindings(new GLFWBindingsContext());
            _bindingsLoadedThreadId = threadId;
        }
    }

    /// <summary>
    /// Makes the OpenGL context current on this thread.
    /// </summary>
    public unsafe void MakeCurrent()
    {
        EnsureContextValid();
        if (_glfwWindow != null)
        {
            GLFW.MakeContextCurrent(_glfwWindow);
        }
    }

    /// <inheritdoc />
    public unsafe ValueTask InitializeAsync()
    {
        try
        {
            // Initialize GLFW directly (bypasses OpenTK's thread check)
            if (!GLFW.Init())
            {
                _initializationError = "Failed to initialize GLFW";
                _contextValid = false;
                return ValueTask.CompletedTask;
            }
            
            // Set window hints for OpenGL 4.3 Core
            GLFW.WindowHint(WindowHintInt.ContextVersionMajor, 4);
            GLFW.WindowHint(WindowHintInt.ContextVersionMinor, 3);
            GLFW.WindowHint(WindowHintOpenGlProfile.OpenGlProfile, OpenGlProfile.Core);
            GLFW.WindowHint(WindowHintBool.OpenGLForwardCompat, true);
            GLFW.WindowHint(WindowHintBool.Visible, false); // Hidden window
            
            // Create a minimal window with GL context
            var windowHandle = GLFW.CreateWindow(1, 1, "HeadlessGLFixture", null, null);
            if (windowHandle == null)
            {
                _initializationError = "Failed to create GLFW window";
                _contextValid = false;
                GLFW.Terminate();
                return ValueTask.CompletedTask;
            }
            
            // Make context current and load OpenGL bindings
            GLFW.MakeContextCurrent(windowHandle);
            GL.LoadBindings(new GLFWBindingsContext());
            
            // Store window handle for cleanup
            _glfwWindow = windowHandle;
            
            // Query OpenGL info
            GLVersion = GL.GetString(StringName.Version);
            GLRenderer = GL.GetString(StringName.Renderer);

            // Verify we have at least OpenGL 4.3
            GL.GetInteger(GetPName.MajorVersion, out int major);
            GL.GetInteger(GetPName.MinorVersion, out int minor);

            if (major < 4 || (major == 4 && minor < 3))
            {
                _initializationError = $"OpenGL 4.3+ required, got {major}.{minor}";
                _contextValid = false;
            }
            else
            {
                _contextValid = true;
            }
        }
        catch (Exception ex)
        {
            _initializationError = $"Failed to create OpenGL context: {ex.Message}";
            _contextValid = false;
        }

        return ValueTask.CompletedTask;
    }

    /// <inheritdoc />
    public unsafe ValueTask DisposeAsync()
    {
        if (_glfwWindow != null)
        {
            GLFW.DestroyWindow(_glfwWindow);
            _glfwWindow = null;
        }
        
        GLFW.Terminate();
        _contextValid = false;
        return ValueTask.CompletedTask;
    }
}
