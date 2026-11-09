using VanillaGraphicsExpanded.Tests.Support.GPU.Observations;

namespace VanillaGraphicsExpanded.Tests.Support.GPU.Harness;

/// <summary>Composes explicit observation owners under the fixture's original context and manager lifetime.</summary>
public sealed partial class HeadlessGLFixture
{
    private GpuObservations? observations;
    private bool retiringContext;
    private (nint Handle, long Generation) fixtureContext;

    #region Internal API
    /// <summary>Restores this fixture after a test deliberately replaces its registered context lifetime.</summary>
    internal unsafe void RestoreContextRegistration()
    {
        if (_glfwWindow == null || OpenTK.Windowing.GraphicsLibraryFramework.GLFW.GetCurrentContext() != _glfwWindow)
            throw new InvalidOperationException("Select the fixture window before restoring its context registration.");
        // Old preparations keep their original generation and must retire before a new epoch is adopted.
        DisposeObservationPrograms();
        long generation = VanillaGraphicsExpanded.Rendering.Integration.RenderContextRegistry.RegisterCurrent(
            this, static target => ((HeadlessGLFixture)target)._glfwWindow != null);
        fixtureContext = ((nint)_glfwWindow, generation);
    }

    /// <summary>Gets explicit observation services; executable loading requires a separate setup request.</summary>
    internal GpuObservations Observations
    {
        get
        {
            ObjectDisposedException.ThrowIf(retiringContext, this);
            if (!VanillaGraphicsExpanded.Rendering.Integration.RenderContextRegistry.IsCurrentResourceContext(fixtureContext))
                throw new InvalidOperationException("Select this fixture's original physical context before accessing observation services.");
            if (observations?.Programs.IsRetired == true) DisposeObservationPrograms();
            return observations ??= new(new GpuObservationPrograms());
        }
    }
    #endregion

    #region Private
    /// <summary>Retires every original shader owner before its originating manager and context.</summary>
    private void DisposeObservationPrograms()
    {
        var original = observations;
        original?.Programs.Dispose();
        observations = null;
    }
    #endregion
}
