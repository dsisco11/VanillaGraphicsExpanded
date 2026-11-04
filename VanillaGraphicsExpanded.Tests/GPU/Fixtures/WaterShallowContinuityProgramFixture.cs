using VanillaGraphicsExpanded.Rendering.Shaders.Fixtures;

namespace VanillaGraphicsExpanded.Tests.GPU.Fixtures;

/// <summary>Owns the diagnostic and receiver-filter programs shared by shallow-wall numerical cases.</summary>
public sealed class WaterShallowContinuityProgramFixture : IDisposable
{
    private readonly WaterNumericalProgramFixtureOwner<WaterShallowProgramSet> owner;

    #region Public API
    /// <summary>Creates both real programs before each case creates any reduction-program scope.</summary>
    /// <param name="context">The GPU collection context that outlives this class fixture.</param>
    public WaterShallowContinuityProgramFixture(HeadlessGLFixture context)
    {
        owner = new WaterNumericalProgramFixtureOwner<WaterShallowProgramSet>(context,
            static programs => new WaterShallowProgramSet(
                programs.Create<WaterRefractionDiagnosticShaderProgram>(),
                programs.Create<WaterReceiverFilterShaderProgram>()),
            static set =>
            {
                set.Diagnostic.ResetInputsForReuse();
                set.Filter.ResetInputsForReuse();
            },
            static set => set.Diagnostic.Shader.IsEmpty && set.Filter.Shader.IsEmpty);
    }

    /// <summary>Starts one isolated shallow-water case on the class-owned program pair.</summary>
    internal WaterNumericalProgramFixtureOwner<WaterShallowProgramSet>.Lease Acquire() => owner.Acquire();

    /// <summary>Retires the class-owned program pair through the still-live GPU context.</summary>
    public void Dispose() => owner.Dispose();

    /// <summary>Gets the enclosing engine platform for fixture-boundary restoration checks.</summary>
    internal Vintagestory.Client.NoObf.ClientPlatformAbstract? PreviousPlatform => owner.PreviousPlatform;

    /// <summary>Reports whether both shared executables have been retired.</summary>
    internal bool ProgramSetIsRetired => owner.ProgramSetIsRetired;
    #endregion
}

/// <summary>Groups the two programs whose shared frame camera and receiver inputs form one shallow-case lease.</summary>
internal sealed class WaterShallowProgramSet
{
    /// <summary>Gets the water traversal diagnostic executable.</summary>
    internal WaterRefractionDiagnosticShaderProgram Diagnostic { get; }

    /// <summary>Gets the paired receiver-filter diagnostic executable.</summary>
    internal WaterReceiverFilterShaderProgram Filter { get; }

    /// <summary>Creates the fixed program pair owned by one shallow-continuity test class.</summary>
    internal WaterShallowProgramSet(WaterRefractionDiagnosticShaderProgram diagnostic,
        WaterReceiverFilterShaderProgram filter)
    {
        Diagnostic = diagnostic ?? throw new ArgumentNullException(nameof(diagnostic));
        Filter = filter ?? throw new ArgumentNullException(nameof(filter));
    }
}
