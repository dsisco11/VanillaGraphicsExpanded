using VanillaGraphicsExpanded.Rendering.Shaders.Fixtures;
using Vintagestory.API.Client;
using Vintagestory.Client;
using Vintagestory.Client.NoObf;


namespace VanillaGraphicsExpanded.Tests.Component.Water.Fixtures;

/// <summary>Owns the numerical water diagnostic executable for one test class and resets it between cases.</summary>
public sealed class WaterRefractionBudgetProgramFixture : IDisposable
{
    private readonly HeadlessGLFixture context;
    private readonly ComponentShaderPrograms programs;
    private readonly WaterRefractionDiagnosticShaderProgram program;
    private readonly object lifetimeGate = new();
    private readonly ClientPlatformAbstract? previousPlatform;
    private bool leaseActive;
    private bool disposed;

    #region Public API
    /// <summary>Creates the shared executable before per-case shader platform scopes can be installed.</summary>
    /// <param name="context">The collection-owned OpenGL context that outlives this class fixture.</param>
    public WaterRefractionBudgetProgramFixture(HeadlessGLFixture context)
    {
        this.context = context ?? throw new ArgumentNullException(nameof(context));
        context.EnsureContextValid();
        previousPlatform = ScreenManager.Platform;
        programs = new ComponentShaderPrograms();
        try
        {
            program = programs.Create<WaterRefractionDiagnosticShaderProgram>();
            program.ResetInputsForReuse();
        }
        catch
        {
            programs.Dispose();
            throw;
        }
    }

    /// <summary>Reserves the class-owned program and starts the next case from complete zero defaults.</summary>
    internal Lease Acquire()
    {
        lock (lifetimeGate)
        {
            if (disposed) throw new ObjectDisposedException(nameof(WaterRefractionBudgetProgramFixture));
            if (leaseActive)
                throw new InvalidOperationException("The shared water diagnostic program already has an active case lease.");

            leaseActive = true;
            try
            {
                program.ResetInputsForReuse();
                return new Lease(this);
            }
            catch
            {
                leaseActive = false;
                throw;
            }
        }
    }

    /// <summary>Retires the shared executable while the collection context is still current.</summary>
    public void Dispose()
    {
        lock (lifetimeGate)
        {
            if (disposed) return;
            if (leaseActive)
                throw new InvalidOperationException("The shared water diagnostic program cannot retire during an active case lease.");

            // Commit retirement only after rejecting active leases, so the fixture can still retire later.
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

    /// <summary>Returns the engine platform expected after this fixture restores its enclosing scope.</summary>
    internal ClientPlatformAbstract? PreviousPlatform => previousPlatform;

    /// <summary>Reports whether disposal retired the native shader handle owned by this fixture.</summary>
    internal bool ProgramIsRetired => program.Shader.IsEmpty;
    #endregion

    #region Private
    /// <summary>Clears shared inputs before another case can acquire the executable.</summary>
    private void Release()
    {
        lock (lifetimeGate)
        {
            if (!leaseActive) return;
            try
            {
                program.ResetInputsForReuse();
            }
            finally
            {
                leaseActive = false;
            }
        }
    }

    /// <summary>Represents exclusive use of the shared executable by one numerical test case.</summary>
    internal sealed class Lease : IDisposable
    {
        private WaterRefractionBudgetProgramFixture? owner;

        /// <summary>Creates a lease after the fixture has reset its retained inputs.</summary>
        /// <param name="owner">The fixture responsible for clearing inputs when the lease ends.</param>
        internal Lease(WaterRefractionBudgetProgramFixture owner) => this.owner = owner;

        /// <summary>Gets the shared executable while this case owns the lease.</summary>
        internal WaterRefractionDiagnosticShaderProgram Program
            => owner is null ? throw new ObjectDisposedException(nameof(Lease)) : owner.program;

        /// <summary>Clears borrowed and typed inputs once, even when the case exits through an assertion failure.</summary>
        public void Dispose() => Interlocked.Exchange(ref owner, null)?.Release();
    }
    #endregion
}
