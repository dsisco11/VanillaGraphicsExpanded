using VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;
using OpenTK.Graphics.OpenGL;
using OpenTK.Windowing.Desktop;
using OpenTK.Windowing.GraphicsLibraryFramework;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Rendering.Integration;

namespace VanillaGraphicsExpanded.Tests.GPU.Fixtures;

/// <summary>Provides supported non-unit inherited line width for real shader workload contracts.</summary>
internal sealed unsafe class PsoWorkloadContext : IDisposable
{
    private readonly HeadlessGLFixture fixture;
    private Window* window;

    #region Public API
    /// <summary>Creates an isolated non-forward-compatible context without replacing collection ownership.</summary>
    internal PsoWorkloadContext(HeadlessGLFixture fixture)
    {
        this.fixture = fixture; fixture.MakeCurrent();
        // Shared test allocators and sampler names belong to their current, unshared context.
        TestUniformRing.Dispose(); GpuSamplers.Dispose();
        GLFW.DefaultWindowHints(); GLFW.WindowHint(WindowHintInt.ContextVersionMajor, 4);
        GLFW.WindowHint(WindowHintInt.ContextVersionMinor, 5); GLFW.WindowHint(WindowHintOpenGlProfile.OpenGlProfile, OpenGlProfile.Compat);
        GLFW.WindowHint(WindowHintBool.Visible, false); GLFW.WindowHint(WindowHintBool.OpenGLForwardCompat, false);
        window = GLFW.CreateWindow(32, 32, "PSO workload contracts", null, null);
        Assert.True(window != null);
        GLFW.MakeContextCurrent(window); GL.LoadBindings(new GLFWBindingsContext());
        RenderContextRegistry.RegisterCurrent(this, owner => ((PsoWorkloadContext)owner).window != null);
        GpuSupport.EnsureCurrentContext(); OpenGlStateCache.Current.InvalidateAll();
        TestUniformRing.BeginFrame();
    }

    /// <summary>Retires the isolated native context before returning to the collection fixture.</summary>
    public void Dispose()
    {
        TestUniformRing.Dispose(); GpuSamplers.Dispose();
        RenderContextRegistry.Retire(this); GLFW.DestroyWindow(window); window = null;
        fixture.MakeCurrent(); GpuSupport.EnsureCurrentContext(); OpenGlStateCache.Current.InvalidateAll();
    }
    #endregion
}
