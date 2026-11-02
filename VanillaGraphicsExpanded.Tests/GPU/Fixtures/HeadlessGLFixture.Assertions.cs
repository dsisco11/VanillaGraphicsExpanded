using VanillaGraphicsExpanded.Tests.GPU.Assertions;

namespace VanillaGraphicsExpanded.Tests.GPU.Fixtures;

/// <summary>Composes explicit observation owners under the fixture's original context and manager lifetime.</summary>
public sealed partial class HeadlessGLFixture
{
    private GpuObservations? observations;
    private bool retiringContext;
    private (nint Handle, long Generation) fixtureContext;

    #region Internal API
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
