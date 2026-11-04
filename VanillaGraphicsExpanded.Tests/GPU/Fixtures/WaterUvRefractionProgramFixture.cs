using VanillaGraphicsExpanded.Rendering.Shaders.Fixtures;

namespace VanillaGraphicsExpanded.Tests.GPU.Fixtures;

/// <summary>Owns one UV-receiver executable for the numerical projection cases in a single test class.</summary>
public sealed class WaterUvRefractionProgramFixture : IDisposable
{
    private readonly WaterNumericalProgramFixtureOwner<WaterUvRefractionShaderProgram> owner;

    #region Public API
    /// <summary>Creates the real UV program before any per-case reduction-program scope.</summary>
    /// <param name="context">The GPU collection context that outlives this class fixture.</param>
    public WaterUvRefractionProgramFixture(HeadlessGLFixture context)
    {
        owner = new WaterNumericalProgramFixtureOwner<WaterUvRefractionShaderProgram>(context,
            static programs => programs.Create<WaterUvRefractionShaderProgram>(),
            static program => program.ResetInputsForReuse(),
            static program => program.Shader.IsEmpty);
    }

    /// <summary>Starts one isolated UV projection case on the class-owned executable.</summary>
    internal WaterNumericalProgramFixtureOwner<WaterUvRefractionShaderProgram>.Lease Acquire() => owner.Acquire();

    /// <summary>Retires the class-owned executable through the still-live GPU context.</summary>
    public void Dispose() => owner.Dispose();

    /// <summary>Gets the enclosing engine platform for fixture-boundary restoration checks.</summary>
    internal Vintagestory.Client.NoObf.ClientPlatformAbstract? PreviousPlatform => owner.PreviousPlatform;

    /// <summary>Reports whether the shared executable has been retired.</summary>
    internal bool ProgramSetIsRetired => owner.ProgramSetIsRetired;
    #endregion
}
