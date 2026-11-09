using VanillaGraphicsExpanded.LumOn;
using VanillaGraphicsExpanded.LumOn.Shaders;
using VanillaGraphicsExpanded.Tests.Support.GPU.Observations;
using VanillaGraphicsExpanded.Tests.Component.LumOn.Fixtures;
using Xunit;

namespace VanillaGraphicsExpanded.Tests.Integration.LumOn;

/// <summary>Verifies near-field shader variant linkage across the OpenGL boundary.</summary>
[Collection("GPU")]
[Trait("Category", "GpuIntegration")]
public sealed partial class NearFieldNativeBoundaryTests : NearFieldShaderTestBase, IClassFixture<NearFieldNumericalProgramFixture>
{
    #region Public API
    /// <summary>Shares the headless GPU context and class-scoped numerical program fixture.</summary>
    public NearFieldNativeBoundaryTests(HeadlessGLFixture fixture, NearFieldNumericalProgramFixture numericalPrograms)
        : base(fixture, numericalPrograms) { }

    /// <summary>Production near-field tracing compiles alongside importance selection, with and without a world cache.</summary>
    [Theory]
    [InlineData("0")]
    [InlineData("1")]
    public void NearField_CompilesWithImportanceSelection(string worldCache)
    {
        // Arrange: Each case selects a declared combination of the production trace shader.
        // Act: Link that variant through the live OpenGL program path.
        // Assert: Program creation completes without a native link failure.
        EnsureShaderTestAvailable();
        _ = Programs.Create<LumOnScreenProbeAtlasTraceShaderProgram>(shader =>
        {
            shader.NearField = true;
            shader.ImportanceSampling = true;
            shader.WorldProbes = worldCache == "1";
        });
    }
    #endregion

    #region Private
    /// <summary>Prepares the comparison observation used by the native upload readback.</summary>
    private protected override void PrepareObservations(GpuObservationPrograms programs)
    {
        base.PrepareObservations(programs);
        programs.Prepare("compare", 2, 0);
    }
    #endregion
}
