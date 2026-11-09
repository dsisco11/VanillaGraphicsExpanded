using VanillaGraphicsExpanded.Rendering.Integration;
using VanillaGraphicsExpanded.Tests.Component.LumOn.Fixtures;

namespace VanillaGraphicsExpanded.Tests.Component.LumOn;

/// <summary>Checks that numerical executable owners reject retired resource-context generations.</summary>
[Collection("OperationContext")]
[Trait("Category", "GpuIntegration")]
public sealed class NearFieldNumericalProgramAuthorityTests(HeadlessGLFixture context)
{
    #region Public API
    /// <summary>A replacement registry generation cannot authorize a class owner's first numerical executable.</summary>
    [Fact]
    public void OwnerRejectsRetiredOriginalContextGeneration()
    {
        context.EnsureContextValid();
        var owner = new NearFieldNumericalProgramFixture(context);
        var replacement = new object();
        var key = new NearFieldTraceProgramKey(true, 1, 64, true, 1, 8, 16, 4);
        try
        {
            // Arrange a new registry generation in place of the original owner's captured authority.
            RenderContextRegistry.Retire(context);
            RenderContextRegistry.RegisterCurrent(replacement, static _ => true);

            // Act by asking the owner to acquire its first numerical program under the retired generation.
            // Assert rejection happens before the owner can prepare a native executable.
            Assert.Throws<InvalidOperationException>(() => owner.Acquire(key));
        }
        finally
        {
            // Restore collection authority before disposing the unprepared owner shell.
            RenderContextRegistry.Retire(replacement);
            context.RestoreContextRegistration();
            owner.Dispose();
        }
    }

    #endregion

}
