using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Rendering.RHI;

namespace VanillaGraphicsExpanded.Tests.GPU.Helpers;

/// <summary>Exercises explicit mutable native-program integration without reintroducing wrapper-owned execution.</summary>
internal static class NativeProgramFixture
{
    #region Internal API
    /// <summary>Submits and completes the same manager-owned program through the final backend publication operation.</summary>
    internal static bool Link(GpuProgramObject owner, out string diagnostics)
    {
        var backend = new VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL.OpenGLBackend(StateCache.Current);
        backend.SubmitProgramExecutable(owner);
        return backend.CompleteProgramLink(owner, out diagnostics);
    }
    #endregion
}
