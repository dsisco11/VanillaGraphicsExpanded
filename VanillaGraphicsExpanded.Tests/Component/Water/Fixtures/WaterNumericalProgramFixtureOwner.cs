using VanillaGraphicsExpanded.Rendering.Shaders.Fixtures;
using Vintagestory.API.Client;
using Vintagestory.Client;
using Vintagestory.Client.NoObf;


namespace VanillaGraphicsExpanded.Tests.Component.Water.Fixtures;

/// <summary>Owns one class fixture's real shader set and serializes reset, lease, and retirement transitions.</summary>
internal sealed class WaterNumericalProgramFixtureOwner<TProgramSet> : IDisposable
{
    private readonly HeadlessGLFixture context;
    private readonly ComponentShaderPrograms programs;
    private readonly TProgramSet programSet;
    private readonly Action<TProgramSet> reset;
    private readonly Func<TProgramSet, bool> isRetired;
    private readonly object lifetimeGate = new();
    private readonly ClientPlatformAbstract? previousPlatform;
    private bool leaseActive;
    private bool disposed;

    #region Public API
    /// <summary>Creates the complete class-owned program set before per-case shader scopes are installed.</summary>
    /// <param name="context">The GPU collection context, which outlives this class fixture.</param>
    /// <param name="createProgramSet">Creates exactly the programs used by the owning numerical test class.</param>
    /// <param name="reset">Clears every case-specific input before and after each lease.</param>
    /// <param name="isRetired">Reports whether all native program handles were released.</param>
    internal WaterNumericalProgramFixtureOwner(HeadlessGLFixture context,
        Func<ComponentShaderPrograms, TProgramSet> createProgramSet,
        Action<TProgramSet> reset,
        Func<TProgramSet, bool> isRetired)
    {
        this.context = context ?? throw new ArgumentNullException(nameof(context));
        ArgumentNullException.ThrowIfNull(createProgramSet);
        this.reset = reset ?? throw new ArgumentNullException(nameof(reset));
        this.isRetired = isRetired ?? throw new ArgumentNullException(nameof(isRetired));
        context.EnsureContextValid();
        previousPlatform = ScreenManager.Platform;
        programs = new ComponentShaderPrograms();
        try
        {
            programSet = createProgramSet(programs);
            reset(programSet);
        }
        catch
        {
            programs.Dispose();
            throw;
        }
    }

    /// <summary>Reserves the live class-owned program set and starts with zeroed inputs.</summary>
    internal Lease Acquire()
    {
        lock (lifetimeGate)
        {
            if (disposed) throw new ObjectDisposedException(nameof(WaterNumericalProgramFixtureOwner<TProgramSet>));
            if (leaseActive)
                throw new InvalidOperationException("The numerical shader set already has an active case lease.");

            leaseActive = true;
            try
            {
                reset(programSet);
                return new Lease(this);
            }
            catch
            {
                leaseActive = false;
                throw;
            }
        }
    }

    /// <summary>Retires every executable while its originating context and outer platform scope remain active.</summary>
    public void Dispose()
    {
        lock (lifetimeGate)
        {
            if (disposed) return;
            if (leaseActive)
                throw new InvalidOperationException("The numerical shader set cannot retire during an active case lease.");

            // Rejecting an active lease leaves the fixture live for a later, valid retirement attempt.
            disposed = true;
            try
            {
                context.EnsureContextValid();
            }
            finally
            {
                programs.Dispose();
            }
        }
    }

    /// <summary>Gets the engine platform that must be restored after this fixture releases its scope.</summary>
    internal ClientPlatformAbstract? PreviousPlatform => previousPlatform;

    /// <summary>Reports whether every shader in this fixture's set has released its native executable.</summary>
    internal bool ProgramSetIsRetired => isRetired(programSet);
    #endregion

    #region Private
    /// <summary>Clears the complete program set before allowing another class case to acquire it.</summary>
    private void Release()
    {
        lock (lifetimeGate)
        {
            if (!leaseActive) return;
            try
            {
                reset(programSet);
            }
            finally
            {
                leaseActive = false;
            }
        }
    }
    #endregion

    /// <summary>Provides exclusive case access and becomes unusable once released.</summary>
    internal sealed class Lease : IDisposable
    {
        private WaterNumericalProgramFixtureOwner<TProgramSet>? owner;

        /// <summary>Creates a lease after the owner clears its shared input set.</summary>
        /// <param name="owner">The class fixture that resets inputs when this case finishes.</param>
        internal Lease(WaterNumericalProgramFixtureOwner<TProgramSet> owner) => this.owner = owner;

        /// <summary>Gets the program set while this case owns the fixture lease.</summary>
        internal TProgramSet ProgramSet
            => owner is null ? throw new ObjectDisposedException(nameof(Lease)) : owner.programSet;

        /// <summary>Resets shared inputs exactly once, including when assertions fail.</summary>
        public void Dispose() => Interlocked.Exchange(ref owner, null)?.Release();
    }
}
