using System;
using System.Threading.Tasks;
using OpenTK.Graphics.OpenGL;
using OpenTK.Mathematics;
using OpenTK.Windowing.Common;
using OpenTK.Windowing.Desktop;
using OpenTK.Windowing.GraphicsLibraryFramework;
using Xunit;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;

namespace VanillaGraphicsExpanded.Tests.GPU.Fixtures;

/// <summary>
/// xUnit fixture that creates a headless OpenGL 4.5 context for GPU tests.
/// Uses OpenTK.Windowing.Desktop to create a hidden window with an OpenGL context.
/// This uses the same OpenTK libraries as the production code, ensuring full compatibility.
/// </summary>
/// <remarks>
/// Usage:
/// <code>
/// [Collection("GPU")]
/// [Trait("Category", "GpuIntegration")]
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
public sealed partial class HeadlessGLFixture : IAsyncLifetime
{
    private unsafe Window* _glfwWindow;
    private bool _contextValid;
    private string? _initializationError;
    private int _bindingsLoadedThreadId = -1;
    private RHIResourceManager? _resourceManager;
    private readonly IDisposable shaderCache = Rendering.ProgramBinaries.DriverProgramCache.UseDefaultStoreForTesting(TestShaderCache.Store);
    private static readonly Lazy<HarmonyLib.Harmony> bindingHooks = new(() =>
    {
        var harmony = new HarmonyLib.Harmony("Tests.OpenTKBindingLoader");
        harmony.CreateClassProcessor(typeof(OpenGLBindingLoaderHook)).Patch();
        return harmony;
    });

#if DEBUG
    private DebugProc? _debugCallback;
#endif

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
    public void EnsureContextValid(bool prepareAssertions = true)
    {
        Assert.SkipWhen(!_contextValid, $"OpenGL context not available: {_initializationError ?? "Unknown error"}");
        _ = bindingHooks.Value;

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
            OpenTK.Graphics.OpenGL4.GL.LoadBindings(new GLFWBindingsContext());
            _bindingsLoadedThreadId = threadId;

            // Loading bindings is an initialization boundary, not permission to erase an earlier failure.
            Assert.Equal(OpenTK.Graphics.OpenGL.ErrorCode.NoError, GL.GetError());
        }
        // Capability discovery is fixture initialization, never part of an intercepted native operation.
        GpuSupport.EnsureCurrentContext();
        OpenGLBindingInterception.Install();
        if (prepareAssertions) PrepareAssertionPrograms();
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

    #region Public API - Resource Cleanup

    /// <summary>Starts deferred resource disposal for tests that create owning attachments.</summary>
    public void InitializeResourceDisposal()
    {
        EnsureContextValid();
        // Reuse the context service; explicit lifecycle tests can retire it before installing their own.
        var manager = RHIResourceManagerSystem.EnsureCurrentContext();
        _resourceManager = manager;
    }

    /// <summary>Ends an explicitly tested resource-service lifetime while keeping the native fixture context available.</summary>
    public void RetireResourceServices()
    {
        var failures = new System.Collections.Generic.List<Exception>();
        AttemptCleanup(DisposeAssertionPrograms, failures);
        // Upload buffers and fences must retire while their original deletion service is available.
        AttemptCleanup(TextureStreamingSystem.Dispose, failures);
        AttemptCleanup(TestUniformRing.Dispose, failures);
        var current = RHIResourceManagerSystem.CurrentManager;
        AttemptCleanup(RHIResourceManagerSystem.Shutdown, failures);
        if (current is not null) AttemptCleanup(current.Dispose, failures);
        if (_resourceManager is not null && !ReferenceEquals(current, _resourceManager))
            AttemptCleanup(_resourceManager.Dispose, failures);
        _resourceManager = null;
        if (failures.Count != 0) throw new AggregateException("GPU service retirement failed.", failures);
    }

    /// <summary>Drains per-test resources while retaining the fixture's manager and production shader owners.</summary>
    public void CleanupGpuResources()
    {
        if (!_contextValid) return;
        EnsureContextValid(prepareAssertions: false);

        // The fixture manager is intentionally unticked: disposal uses the current context without
        // registering an engine renderer or capturing a thread that xUnit may subsequently change.
        TextureStreamingSystem.Dispose();
        TestUniformRing.Dispose();
        var current = RHIResourceManagerSystem.CurrentManager;
        current?.ResourceDisposals?.DrainPending();
    }

    #endregion

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

            // Set window hints for OpenGL 4.5 Core
            GLFW.WindowHint(WindowHintInt.ContextVersionMajor, 4);
            GLFW.WindowHint(WindowHintInt.ContextVersionMinor, 5);
            GLFW.WindowHint(WindowHintOpenGlProfile.OpenGlProfile, OpenGlProfile.Core);
            GLFW.WindowHint(WindowHintBool.OpenGLForwardCompat, true);
            GLFW.WindowHint(WindowHintBool.OpenGLDebugContext, true);
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
            OpenTK.Graphics.OpenGL4.GL.LoadBindings(new GLFWBindingsContext());

            // Store window handle for cleanup
            _glfwWindow = windowHandle;
            Rendering.Integration.RenderContextRegistry.RegisterCurrent(this, static target => ((HeadlessGLFixture)target)._glfwWindow != null);

            // Query OpenGL info
            GLVersion = GL.GetString(StringName.Version);
            GLRenderer = GL.GetString(StringName.Renderer);

            // Verify we have at least OpenGL 4.5
            GL.GetInteger(GetPName.MajorVersion, out int major);
            GL.GetInteger(GetPName.MinorVersion, out int minor);

            if (major < 4 || (major == 4 && minor < 5))
            {
                _initializationError = $"OpenGL 4.5+ required, got {major}.{minor}";
                _contextValid = false;
            }
            else
            {
                _contextValid = true;

#if DEBUG
                TryEnableDebugCallback();
#endif
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
        retiringContext = true;
        var failures = new System.Collections.Generic.List<Exception>();
        try
        {
            if (_glfwWindow != null)
            {
                // Collections own separate, unshared contexts. Retire the test ring's
                // buffer names and persistent mapping before another context can reuse them.
                try
                {
                    GLFW.MakeContextCurrent(_glfwWindow);
                    if (GLFW.GetCurrentContext() != _glfwWindow)
                        throw new InvalidOperationException("Could not select the fixture context for native retirement.");
                    GL.LoadBindings(new GLFWBindingsContext());
                    OpenTK.Graphics.OpenGL4.GL.LoadBindings(new GLFWBindingsContext());
                    // A failed release must not prevent the remaining original owners from retiring.
                    AttemptCleanup(DisposeAssertionPrograms, failures);
                    AttemptCleanup(TestUniformRing.Dispose, failures);
                    AttemptCleanup(VanillaGraphicsExpanded.PBR.Materials.Energy.MicrofacetEnergyLookup.Retire, failures);
                    AttemptCleanup(GpuSamplers.Dispose, failures);
                    AttemptCleanup(RetireResourceServices, failures);
                }
                catch (Exception error) { failures.Add(error); }
                // Context setup can itself fail. Retire its namespace and destroy its window regardless;
                // never run native owner cleanup against whichever foreign context remained selected.
                AttemptCleanup(() => Rendering.Integration.RenderContextRegistry.Retire(this), failures);
                // Retired-context owners discard their original names without native calls. This also
                // clears service authority if selecting the window failed before ordinary cleanup.
                AttemptCleanup(RetireResourceServices, failures);
                AttemptCleanup(() => GLFW.DestroyWindow(_glfwWindow), failures);
                _glfwWindow = null;
            }

            AttemptCleanup(GLFW.Terminate, failures);
            _contextValid = false;
        }
        finally
        {
            if (ReferenceEquals(currentFixture, this)) currentFixture = null;
            AttemptCleanup(shaderCache.Dispose, failures);
        }
        if (failures.Count != 0) throw new AggregateException("GPU fixture retirement failed.", failures);
        return ValueTask.CompletedTask;
    }

    /// <summary>Preserves cleanup failures while allowing remaining ordered owners and native context stages to retire.</summary>
    private static void AttemptCleanup(Action cleanup, System.Collections.Generic.List<Exception> failures)
    {
        try { cleanup(); }
        catch (Exception error) { failures.Add(error); }
    }

#if DEBUG
    private void TryEnableDebugCallback()
    {
        try
        {
            // Only attempt debug output when KHR_debug is supported; otherwise these calls can
            // leave GL errors queued without throwing, which breaks tests (and can crash hosts
            // that treat GL errors as fatal).
            if (!VanillaGraphicsExpanded.Rendering.GlExtensions.Supports("GL_KHR_debug"))
            {
                return;
            }

            _debugCallback ??= DebugMessageCallback;

            GL.Enable(EnableCap.DebugOutput);
            GL.Enable(EnableCap.DebugOutputSynchronous);

            GL.DebugMessageCallback(_debugCallback, IntPtr.Zero);
            GL.DebugMessageControl(
                DebugSourceControl.DontCare,
                DebugTypeControl.DontCare,
                DebugSeverityControl.DontCare,
                0,
                Array.Empty<int>(),
                true);

            // Drain any errors caused by best-effort setup.
            while (GL.GetError() != OpenTK.Graphics.OpenGL.ErrorCode.NoError) { }
        }
        catch
        {
            // Best-effort: debug output depends on driver/context settings.
        }
    }

    private static void DebugMessageCallback(
        DebugSource source,
        DebugType type,
        int id,
        DebugSeverity severity,
        int length,
        IntPtr message,
        IntPtr userParam)
    {
        try
        {
            string msg = System.Runtime.InteropServices.Marshal.PtrToStringAnsi(message, length) ?? string.Empty;
            Console.WriteLine($"[GLDBG] {severity} {type} {source} id={id}: {msg}");
        }
        catch
        {
        }
    }
#endif
}
