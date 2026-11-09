using VanillaGraphicsExpanded.Rendering.Shaders.Fixtures;


namespace VanillaGraphicsExpanded.Tests.Component.Water.Fixtures;

/// <summary>Owns one pixel-normal executable for the numerical refraction cases in a single test class.</summary>
public sealed class WaterPixelNormalRefractionProgramFixture : IDisposable
{
    private readonly WaterNumericalProgramFixtureOwner<WaterPixelNormalRefractionShaderProgram> owner;

    #region Public API
    /// <summary>Creates the real pixel-normal program before any per-case reduction-program scope.</summary>
    /// <param name="context">The GPU collection context that outlives this class fixture.</param>
    public WaterPixelNormalRefractionProgramFixture(HeadlessGLFixture context)
    {
        owner = new WaterNumericalProgramFixtureOwner<WaterPixelNormalRefractionShaderProgram>(context,
            static programs => programs.Create<WaterPixelNormalRefractionShaderProgram>(),
            static program => program.ResetInputsForReuse(),
            static program => program.Shader.IsEmpty);
    }

    /// <summary>Starts one isolated pixel-normal case on the class-owned executable.</summary>
    internal WaterNumericalProgramFixtureOwner<WaterPixelNormalRefractionShaderProgram>.Lease Acquire() => owner.Acquire();

    /// <summary>Retires the class-owned executable through the still-live GPU context.</summary>
    public void Dispose() => owner.Dispose();

    /// <summary>Gets the enclosing engine platform for fixture-boundary restoration checks.</summary>
    internal Vintagestory.Client.NoObf.ClientPlatformAbstract? PreviousPlatform => owner.PreviousPlatform;

    /// <summary>Reports whether the shared executable has been retired.</summary>
    internal bool ProgramSetIsRetired => owner.ProgramSetIsRetired;
    #endregion
}
