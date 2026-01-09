using System;
using System.Threading.Tasks;
using Silk.NET.OpenGL;
using Silk.NET.Windowing;
using Xunit;

namespace VanillaGraphicsExpanded.Tests.GPU.Fixtures;

/// <summary>
/// xUnit fixture that creates a headless OpenGL 4.3 context for GPU tests.
/// Uses Silk.NET to create a hidden window with an OpenGL context.
/// </summary>
/// <remarks>
/// Usage:
/// <code>
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
    private IWindow? _window;
    private GL? _gl;
    private bool _contextValid;
    private string? _initializationError;

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
    /// The Silk.NET OpenGL API instance. Use this for all GL calls in tests.
    /// </summary>
    public GL? GL => _gl;

    /// <summary>
    /// Ensures the GL context is valid; throws skip exception if not.
    /// Call this at the start of tests that require GPU.
    /// </summary>
    public void EnsureContextValid()
    {
        Assert.SkipWhen(!_contextValid, $"OpenGL context not available: {_initializationError ?? "Unknown error"}");
    }

    /// <summary>
    /// Makes the OpenGL context current on this thread.
    /// </summary>
    public void MakeCurrent()
    {
        EnsureContextValid();
        _window?.MakeCurrent();
    }

    /// <inheritdoc />
    public ValueTask InitializeAsync()
    {
        try
        {
            // Configure window options for headless rendering
            var options = WindowOptions.Default with
            {
                Size = new Silk.NET.Maths.Vector2D<int>(1, 1), // Minimal size
                Title = "HeadlessGLFixture",
                WindowState = WindowState.Minimized,
                IsVisible = false,
                API = new GraphicsAPI(ContextAPI.OpenGL, ContextProfile.Core, ContextFlags.ForwardCompatible, new APIVersion(4, 3))
            };

            // Create the window (this also creates the GL context)
            _window = Window.Create(options);
            
            // Initialize the window (required before GL calls)
            _window.Initialize();

            // Make context current
            _window.MakeCurrent();

            // Get Silk.NET GL instance
            _gl = GL.GetApi(_window);

            // Query OpenGL info
            GLVersion = _gl.GetStringS(StringName.Version);
            GLRenderer = _gl.GetStringS(StringName.Renderer);

            // Verify we have at least OpenGL 4.3
            _gl.GetInteger(GetPName.MajorVersion, out int major);
            _gl.GetInteger(GetPName.MinorVersion, out int minor);

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
    public ValueTask DisposeAsync()
    {
        _gl?.Dispose();
        _gl = null;

        if (_window != null)
        {
            _window.Close();
            _window.Dispose();
            _window = null;
        }

        _contextValid = false;
        return ValueTask.CompletedTask;
    }
}
