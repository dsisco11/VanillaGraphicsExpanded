using VanillaGraphicsExpanded.LumOn;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Rendering.Integration;
using VanillaGraphicsExpanded.Rendering.RHI;
using Vintagestory.Client;
using Vintagestory.Client.NoObf;

namespace VanillaGraphicsExpanded.Tests.GPU.Fixtures;

/// <summary>Owns numerical near-field executables for one test class and clears all case bindings between traces.</summary>
public sealed class NearFieldNumericalProgramFixture : IDisposable
{
    private readonly ComponentShaderPrograms programs;
    private readonly (nint Handle, long Generation) origin;
    private readonly RHIResourceManager manager;
    private readonly ClientPlatformAbstract? previousPlatform;
    private readonly VgeFrameUniformBuffer captureCamera;
    private readonly Dictionary<NearFieldTraceProgramKey, InspectableNearFieldTraceShaderProgram> tracePrograms = [];
    private readonly Dictionary<NearFieldTraceProgramKey, (RHISamplerBinding[] Samplers, RHIBufferBinding[] Buffers)> resetBindings = [];
    private readonly object lifetimeGate = new();
    private Lease? activeLease;
    private bool leaseActive;
    private bool disposed;

    #region Public API
    /// <summary>Creates the class-owned program manager under the collection's original context.</summary>
    /// <param name="context">The GPU collection context that outlives this test class.</param>
    public NearFieldNumericalProgramFixture(HeadlessGLFixture context)
    {
        ArgumentNullException.ThrowIfNull(context);
        context.EnsureContextValid();
        origin = RenderContextRegistry.Current();
        manager = RHIResourceManagerSystem.EnsureCurrentContext();
        previousPlatform = ScreenManager.Platform;
        programs = new ComponentShaderPrograms();
        captureCamera = new VgeFrameUniformBuffer();
    }

    /// <summary>Releases programs only while their original live context still authorizes resource disposal.</summary>
    public void Dispose()
    {
        lock (lifetimeGate)
        {
            if (disposed) return;
            if (leaseActive)
                throw new InvalidOperationException("Near-field programs cannot retire during an active trace lease.");
            if (tracePrograms.Count != 0) RequireRetirementContext();
            try { programs.Dispose(); }
            finally
            {
                captureCamera.Dispose();
                disposed = true;
            }
        }
    }
    #endregion

    #region Internal API
    /// <summary>Acquires the compiled variant and resets every retained variant before it can borrow case inputs.</summary>
    internal Lease Acquire(NearFieldTraceProgramKey key)
    {
        lock (lifetimeGate)
        {
            RequireContext();
            if (leaseActive)
                throw new InvalidOperationException("A near-field trace already owns the numerical program lease.");
            leaseActive = true;
            try
            {
                // The immutable key selects a class-owned executable; create it once and retain its native program across leases.
                if (!tracePrograms.TryGetValue(key, out var program))
                {
                    program = programs.Create<InspectableNearFieldTraceShaderProgram>(shader => key.Configure(shader));
                    tracePrograms.Add(key, program);
                }
                // Clear every retained variant before capturing default resource selections and granting case ownership.
                ResetInputs();
                if (!resetBindings.ContainsKey(key))
                {
                    resetBindings.Add(key, CaptureResourceSelections(program));
                }
                var lease = new Lease(this, program);
                activeLease = lease;
                return lease;
            }
            catch
            {
                leaseActive = false;
                throw;
            }
        }
    }

    /// <summary>Reports whether all native executables retired with the class fixture.</summary>
    internal bool ProgramsAreRetired => disposed && tracePrograms.Values.All(program => program.Shader.IsEmpty);
    /// <summary>Gets the engine platform that class teardown must restore after disposing the fixture owner.</summary>
    internal ClientPlatformAbstract? PreviousPlatform => previousPlatform;

    /// <summary>Verifies case-owned uniform references and resource selections were cleared after every lease.</summary>
    internal bool CaseInputsAreReset
    {
        get
        {
            lock (lifetimeGate)
            {
                RequireContext();
                if (leaseActive) return false;
                foreach (var (key, program) in tracePrograms)
                {
                    if (!program.HasNoCaseUniformReferences || !resetBindings.TryGetValue(key, out var baseline))
                        return false;
                    var current = CaptureResourceSelections(program);
                    if (!baseline.Samplers.SequenceEqual(current.Samplers)
                        || !baseline.Buffers.SequenceEqual(current.Buffers)) return false;
                }
                return true;
            }
        }
    }

    /// <summary>Holds the exclusive program lease through draw and readback submission.</summary>
    internal sealed class Lease : IDisposable
    {
        private NearFieldNumericalProgramFixture? owner;

        #region Public API
        /// <summary>Releases the program after clearing its borrowed case inputs.</summary>
        public void Dispose() => Interlocked.Exchange(ref owner, null)?.Release(this);
        #endregion

        #region Internal API
        /// <summary>Creates a lease for one compiled shader variant.</summary>
        internal Lease(NearFieldNumericalProgramFixture owner, LumOnScreenProbeAtlasTraceShaderProgram program)
        {
            this.owner = owner;
            Value = program;
        }

        /// <summary>Gets the variant while this trace owns the lease.</summary>
        internal LumOnScreenProbeAtlasTraceShaderProgram Program
            => owner is null ? throw new ObjectDisposedException(nameof(Lease)) : owner.GetProgram(this);

        /// <summary>Stores the program reference behind the owner-checked lease getter.</summary>
        internal LumOnScreenProbeAtlasTraceShaderProgram Value { get; }
        #endregion
    }
    #endregion

    #region Private
    #region Context authority
    /// <summary>Rejects a retired manager or a replacement physical resource context.</summary>
    private void RequireContext()
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        if (manager.IsMetadataRegistryRetired || !RenderContextRegistry.IsCurrentResourceContext(origin))
            throw new InvalidOperationException("Near-field numerical reuse requires its original fixture context and live resource manager.");
    }

    /// <summary>Prevents resource retirement from a different still-live context.</summary>
    private void RequireRetirementContext()
    {
        if (manager.IsMetadataRegistryRetired || !RenderContextRegistry.IsCurrentResourceContext(origin))
            throw new InvalidOperationException("Near-field programs can only retire in their original live resource context.");
    }
    #endregion

    #region Input lifecycle
    /// <summary>Clears every case-owned texture, scene, surface and uniform reference from all variants.</summary>
    private void ResetInputs()
    {
        foreach (var program in tracePrograms.Values)
        {
            program.ProbeAnchors = default;
            program.PrimaryDepth = default;
            program.SurfaceAlbedo = default;
            program.GBufferSurface = default;
            program.ScreenProbeAtlasHistory = default;
            program.ScreenProbeAtlasMetaHistory = default;
            program.ProbeTraceMask = default;
            program.HzbDepth = default;
            program.WorldProbeRadianceAtlas = default;
            program.WorldProbeVis0 = default;
            program.WorldProbeMeta0 = default;
            program.BindNearFieldScene(null);
            program.SetSurfaceLighting(null);
            program.FrameInputs = null;
            program.FrameUniformBuffer = null!;
            program.WorldProbeUniformBuffer = null!;
            program.SuppressWorldProbeRadiance = false;
            program.IndirectTint = new(1, 1, 1);
        }
    }

    /// <summary>Captures resource references with a temporary class-owned camera, restoring the prior camera in all paths.</summary>
    private (RHISamplerBinding[] Samplers, RHIBufferBinding[] Buffers) CaptureResourceSelections(
        InspectableNearFieldTraceShaderProgram program)
    {
        // A neutral class-owned camera lets binding capture inspect resource defaults without copying uniform contents.
        var previous = program.FrameInputs;
        program.FrameInputs = captureCamera;
        try
        {
            var bindings = program.CaptureBindings();
            return (bindings.Samplers.ToArray(), bindings.Buffers.ToArray());
        }
        finally
        {
            program.FrameInputs = previous;
        }
    }
    #endregion

    #region Lease lifecycle
    /// <summary>Validates the active lease and context before returning its compiled program.</summary>
    private LumOnScreenProbeAtlasTraceShaderProgram GetProgram(Lease lease)
    {
        lock (lifetimeGate)
        {
            RequireContext();
            if (!leaseActive || !ReferenceEquals(activeLease, lease))
                throw new ObjectDisposedException(nameof(Lease));
            return lease.Value;
        }
    }

    /// <summary>Clears borrowed inputs before retiring the current exclusive lease.</summary>
    private void Release(Lease lease)
    {
        lock (lifetimeGate)
        {
            if (!leaseActive || !ReferenceEquals(activeLease, lease)) return;
            try
            {
                RequireContext();
                ResetInputs();
            }
            finally
            {
                activeLease = null;
                leaseActive = false;
            }
        }
    }
    #endregion
    #endregion
}
