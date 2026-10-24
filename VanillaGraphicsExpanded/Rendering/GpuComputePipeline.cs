using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.Rendering.Contracts;
using VanillaGraphicsExpanded.Rendering.ProgramBinaries;
using VanillaGraphicsExpanded.Rendering.ShaderCompilation;

namespace VanillaGraphicsExpanded.Rendering;

/// <summary>
/// RAII wrapper around an OpenGL compute program (a linked program containing a compute shader stage).
/// Deletion is deferred to <see cref="GpuResourceManager"/> when available.
/// All methods require a current GL context on the calling thread.
/// </summary>
internal sealed partial class GpuComputePipeline : GpuResource<GpuProgramMetadata>, IDisposable
{
    private int programId;
    protected override (nint Handle, long Generation) NativeProgramContext => ExecutableContext;
    /// <summary>Identifies the single installed native generation.</summary>
    internal ulong ExecutableRevision { get; private set; }
    /// <summary>Identifies the native context owning that generation.</summary>
    internal (nint Handle, long Generation) ExecutableContext { get; private set; }
    private GpuProgramLayout programLayout = GpuProgramLayout.Empty;

    private Action<string>? warn;
    /// <summary>The coherent settings used to create this linked compute executable.</summary>
    internal ShaderSettings? InstalledSettings { get; private set; }

    /// <inheritdoc />
    public override nint ResourceId
    {
        get => programId;
        protected set => programId = (int)value;
    }

    protected override GpuResourceKind ResourceKind => GpuResourceKind.Program;

    /// <summary>
    /// Gets the underlying OpenGL program id.
    /// </summary>
    public int ProgramId => programId;

    /// <summary>
    /// Gets cached binding-related program resources (UBO/SSBO bindings, sampler/image units).
    /// Populated on successful creation/link.
    /// </summary>
    public GpuProgramLayout ProgramLayout => programLayout;

    /// <summary>Reports derived preparation invalidation without changing native ownership or identity.</summary>
    internal bool NeedsNativePreparation => programLayout.NeedsNativePreparation;

    /// <summary>Reconciles canonical program changes with retained compute preparation.</summary>
    internal void OnNativeProgramMetadataChanged(GpuMetadataChange change, bool available)
    {
        ulong revision = ResourceManager?.GetResourceRegistration(this)?.MetadataRevision ?? 0;
        programLayout.ObserveNativeMetadata(programId, metadata, revision, change, available);
    }

    /// <summary>
    /// Returns <c>true</c> when the program has a non-zero id and has not been disposed.
    /// </summary>
    public new bool IsValid => base.IsValid;

    /// <summary>Retains preparation policy and acquires metadata for a realized executable.</summary>
    private GpuComputePipeline(int programId, GpuProgramLayout programLayout, Action<string>? warn)
    {
        this.programId = programId;
        if (programId != 0)
        {
            ExecutableRevision = 1;
            ExecutableContext = Integration.RenderContextRegistry.Current();
        }
        this.programLayout = programLayout;
        this.warn = warn;
        if (programId != 0) GpuResourceManager.AcquireWrapperMetadata(this, GpuProgramMetadataReader.Instance, refreshExisting: false);
        if (ResourceManager?.GetResourceRegistration(this) is { MetadataAvailable: true } entry)
            programLayout.UseNativeMetadata(programId, metadata, entry.MetadataRevision);
    }

    /// <summary>
    /// Sets the debug label for this program (debug builds only).
    /// </summary>
    public override void SetDebugName(string? debugName)
    {
#if DEBUG
        if (programId != 0)
        {
            GlDebug.TryLabel(ObjectLabelIdentifier.Program, programId, debugName);
        }
#endif
    }

    /// <summary>
    /// Creates a compute pipeline by linking the provided compiled compute shader module into a new program.
    /// </summary>
    /// <param name="computeShader">A compiled shader module of type <see cref="ShaderType.ComputeShader"/>.</param>
    /// <param name="pipeline">The created pipeline instance on success; otherwise null.</param>
    /// <param name="infoLog">Program link info log from the driver (may be empty).</param>
    /// <param name="debugName">Optional KHR_debug label (debug builds only).</param>
    /// <param name="disposeShaderAfterLink">If true, disposes <paramref name="computeShader"/> after linking.</param>
    /// <param name="retrievableBinary">Requests an executable that can be saved after linking.</param>
    /// <returns>True if linking succeeded.</returns>
    public static bool TryCreate(
        GpuShaderModule computeShader,
        out GpuComputePipeline? pipeline,
        out string infoLog,
        string? debugName = null,
        bool disposeShaderAfterLink = true,
        GpuProgramLayout? layout = null,
        Action<string>? warn = null,
        bool retrievableBinary = false)
    {
        ArgumentNullException.ThrowIfNull(computeShader);

        pipeline = null;
        infoLog = string.Empty;

        if (!computeShader.IsValid || computeShader.ShaderType != ShaderType.ComputeShader)
        {
            infoLog = "Invalid compute shader module.";
            return false;
        }

        int programId = 0;
        GpuProgramObject? candidate = null;
#if DEBUG
        using var errors = new GlDebug.ErrorScope($"{debugName}: compute linking and bindings");
#endif
        try
        {
            candidate = ShaderProgramLink.Submit([computeShader.ShaderId], retrievableBinary
#if DEBUG
                , out _
#endif
                );
            programId = candidate.ProgramId;
#if DEBUG
            GlDebug.TryLabel(ObjectLabelIdentifier.Program, programId, debugName);
            GlDebug.ThrowIfErrors($"{debugName}: compute create/attach/link program={programId}");
#endif
            bool linked = ShaderProgramLink.Validate(candidate, out infoLog);
            GL.DetachShader(programId, computeShader.ShaderId);

            if (disposeShaderAfterLink)
            {
                computeShader.Dispose();
            }

            if (!linked)
            {
                return false;
            }

            pipeline = PrepareLinkedPipeline(candidate, computeShader.BindingContract, layout, warn, debugName);
            return true;
        }
        catch (Exception ex)
        {
            infoLog = (infoLog.Length > 0 ? infoLog + "\n" : string.Empty) + ex.Message;

            pipeline = null;
            return false;
        }
        finally
        {
            candidate?.Dispose();
            // Cover failures before detachment/link status as well as ordinary successful ownership transfer.
            if (disposeShaderAfterLink) computeShader.Dispose();
        }
    }

    #region Linked interface preparation
    /// <summary>Prepares bindings for both normal links and cached executables before ownership transfer.</summary>
    private static GpuComputePipeline PrepareLinkedPipeline(GpuProgramObject candidate, GpuBindingContract? contract,
        GpuProgramLayout? layout, Action<string>? warn, string? debugName)
    {
        int program = candidate.ProgramId;
        // Prepare a separate layout so failure cannot mutate the caller's installed interface.
        var prepared = layout?.CreateCandidate() ?? new GpuProgramLayout();
        if (contract != null) prepared.BinaryInterface = new Spirv.GpuProgramInterface(program, [contract]);
        prepared.ApplyContract(program, warn);
#if DEBUG
        prepared.ValidateContract(program, warn);
#endif
        // Preparation can fail while the candidate still owns deletion. Transfer only
        // at the final construction boundary, whose cleanup covers a failed wrapper.
        candidate.Detach();
        return CompleteOwnedCreation(GpuResourceKind.Program, program, () =>
        {
            var pipeline = new GpuComputePipeline(program, prepared, warn);
            pipeline.SetDebugName(debugName);
            return pipeline;
        });
    }
    #endregion

    /// <summary>Loads an explicitly identified compute selection from a caller-owned binary path.</summary>
    public static bool TryLoadFromSpirv(
        string spirvBinaryPath,
        ShaderSettings settings,
        out GpuComputePipeline? pipeline,
        out string infoLog,
        string? debugName = null,
        GpuProgramLayout? layout = null,
        Action<string>? warn = null)
    {
        pipeline = null;
        try
        {
            // Reject unavailable or empty file inputs before optional cache capability queries touch GL.
            byte[] binary = File.ReadAllBytes(spirvBinaryPath);
            if (binary.Length == 0)
                throw new InvalidOperationException($"Empty SPIR-V binary for '{new ShaderLoadPlan(settings).Stages[0].Stage.Identity}'.");
            // Variant paths are relative to the shader root; relocated standalone binaries use a sibling manifest.
            string selectedPath = new ShaderLoadPlan(settings).Stages[0].BinaryPath.Replace('/', Path.DirectorySeparatorChar);
            string fullPath = Path.GetFullPath(spirvBinaryPath);
            string manifestRoot = fullPath.EndsWith(selectedPath, OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal)
                ? fullPath[..^selectedPath.Length] : Path.GetDirectoryName(fullPath)!;
            ReadOnlySpan<byte> Read(string _) => binary;
            return TryCreateFromBinaryContract(settings, Read, out pipeline, out infoLog, debugName, layout, warn,
                () => ShaderDigestIndexCache.ForFile(Path.Combine(manifestRoot, Spirv.ShaderBinaryDigest.FileName)));
        }
        catch (Exception ex) { infoLog = ex.Message; return false; }

    }

    /// <summary>Resolves and validates all compute settings before reading a borrowed binary or allocating GL objects.</summary>
    private static bool TryCreateFromBinaryContract(ShaderSettings settings,
        Spirv.ShaderAssetReader read, out GpuComputePipeline? pipeline, out string infoLog,
        string? debugName, GpuProgramLayout? layout, Action<string>? warn,
        Func<Spirv.ShaderBinaryDigest.Manifest?> digestIndex, object? assets = null)
    {
        pipeline = null;
        infoLog = string.Empty;
        try
        {
            var plan = new ShaderLoadPlan(settings);
            if (plan.Stages.Count != 1 || plan.Stages[0].Stage.Kind != ShaderStageKind.Compute)
                throw new ArgumentException($"Program '{settings.Contract.Identity}' is not a compute program.");
            using var pending = assets == null ? null : ShaderLinkBatch.Take(assets, PBR.ShaderImportsSystem.DefaultDomain, plan);
            var context = Integration.RenderContextRegistry.Current();
            var inputs = pending?.Inputs ?? DriverProgramCache.Prepare(plan, read, digestIndex);
            if (context != Integration.RenderContextRegistry.Current()) throw new InvalidOperationException("Compute preparation requires its originating context.");
            GpuProgramObject? candidate = pending?.TakeProgram() ?? GpuProgramObject.Create();
            try
            {
                if (pending != null || DriverProgramCache.TryLoad(candidate.ProgramId, inputs))
                {
                    pipeline = PrepareLinkedPipeline(candidate, plan.Stages[0].Stage.Bindings, layout, warn, debugName);
                    if (pending != null)
                    {
                        if (!pending.Cached) DriverProgramCache.Save(pipeline.ProgramId, inputs);
                    }
                }
                else
                {
                    // Discard a rejected executable before returning to the established linking owner.
                    candidate.Dispose();
                    candidate = null;
                    using var module = Spirv.SpirvStageLoader.Load(plan.Stages[0], inputs.Read);
                    if (!TryCreate(module, out pipeline, out infoLog, debugName, layout: layout, warn: warn,
                        retrievableBinary: inputs.Key != null)) return false;
                    DriverProgramCache.Save(pipeline!.ProgramId, inputs);
                }
                pipeline!.InstalledSettings = settings;
                return true;
            }
            finally { candidate?.Dispose(); }
        }
        catch (Exception ex)
        {
            pipeline?.Dispose();
            pipeline = null;
            infoLog = ex.Message;
            return false;
        }
    }

    /// <summary>
    /// Uses this compute pipeline via <c>glUseProgram</c>.
    /// </summary>
    public void Use()
    {
        if (!EnsureReady())
        {
            throw new InvalidOperationException("Compute preparation failed: " + preparationLog);
        }

        RequireActivationContext();
        Integration.NativeShaderHandoff.RequireInactive();
        StateCache.Current.UseProgram(programId);
    }

    /// <summary>
    /// Attempts to use this compute pipeline. Returns <c>false</c> if invalid.
    /// </summary>
    public bool TryUse()
    {
        if (!EnsureReady())
        {
            return false;
        }

        RequireActivationContext();
        Integration.NativeShaderHandoff.RequireInactive();
        StateCache.Current.UseProgram(programId);
        return true;
    }

    /// <summary>
    /// Uses this pipeline and returns a scope that restores the previous program when disposed.
    /// </summary>
    public ProgramScope UseScope()
    {
        if (!EnsureReady()) throw new InvalidOperationException(preparationLog);
        RequireActivationContext();
        RetainPassReference();
        try { return new ProgramScope(new ComputeScope(this, StateCache.Current.UseProgramScope(programId))); }
        catch { ReleasePassReference(); throw; }
    }

    /// <summary>
    /// Dispatches compute work via <c>glDispatchCompute</c>, binding this program for the duration of the call.
    /// </summary>
    public void Dispatch(int numGroupsX, int numGroupsY = 1, int numGroupsZ = 1)
    {
        if (!EnsureReady())
        {
            return;
        }

        using var _ = UseScope();
        GL.DispatchCompute(numGroupsX, numGroupsY, numGroupsZ);
    }

    /// <summary>
    /// Dispatches compute work via <c>glDispatchCompute</c> assuming this program is already current.
    /// </summary>
    public void DispatchBound(int numGroupsX, int numGroupsY = 1, int numGroupsZ = 1)
    {
        if (!IsValid)
        {
            return;
        }

        GL.DispatchCompute(numGroupsX, numGroupsY, numGroupsZ);
    }

    /// <summary>
    /// Dispatches compute work via <c>glDispatchComputeIndirect</c>, binding this program and the provided indirect buffer for the duration of the call.
    /// </summary>
    public void DispatchIndirect(GpuIndirectBuffer indirectBuffer, nint byteOffset = 0)
    {
        ArgumentNullException.ThrowIfNull(indirectBuffer);

        if (!EnsureReady() || !indirectBuffer.IsValid)
        {
            return;
        }

        using var _ = UseScope();
        using var __ = indirectBuffer.BindDispatchScope();
        GL.DispatchComputeIndirect((IntPtr)byteOffset);
    }

    /// <summary>
    /// Inserts an OpenGL memory barrier via <c>glMemoryBarrier</c>.
    /// </summary>
    public static void MemoryBarrier(MemoryBarrierFlags flags)
    {
        GL.MemoryBarrier(flags);
    }

    /// <summary>
    /// Restores the previous program when disposed.
    /// </summary>
    public readonly struct ProgramScope : IDisposable
    {
        private readonly IDisposable? scope;
        /// <summary>Shares the native borrow and binding restoration token between value copies.</summary>
        internal ProgramScope(IDisposable scope) => this.scope = scope;
        /// <summary>Ends native borrowing and restores only the saved binding.</summary>
        public void Dispose() => scope?.Dispose();
    }
    #region Protected API
    /// <summary>Withdraws the deleted executable layout and diagnostic callback.</summary>
    protected override void OnAfterDelete()
    {
        warn = null;

        // Keep the contract but clear the active snapshot for safety.
        programLayout.RebuildCache(programId: 0);
    }
    #endregion

    #region Private
    /// <summary>Rejects an executable from a retired or different native context before binding.</summary>
    private void RequireActivationContext()
    {
        var context = Integration.RenderContextRegistry.Current();
        if (context.Handle == 0 || ExecutableContext != context)
            throw new InvalidOperationException("Compute executable belongs to a different or retired render context.");
    }

    /// <summary>Pairs binding-only restoration with the existing native resource borrow contract.</summary>
    private sealed class ComputeScope : IDisposable
    {
        private readonly GpuComputePipeline owner;
        private readonly StateCache.ProgramScope scope;
        private bool ended;
        /// <summary>Registers the native borrow with the existing scope abandonment path.</summary>
        internal ComputeScope(GpuComputePipeline owner, StateCache.ProgramScope scope)
        {
            this.owner = owner; this.scope = scope;
            scope.OnAbandon(() => { if (ended) return; ended = true; owner.ReleasePassReference(); });
        }
        /// <summary>Restores the binding once and releases the originating native owner's borrow.</summary>
        public void Dispose()
        {
            if (ended) return;
            // An out-of-order rejection must leave both the token and its native borrow usable.
            try { scope.Dispose(); }
            finally
            {
                if (scope.IsEnded) { ended = true; owner.ReleasePassReference(); }
            }
        }
    }
    #endregion
}
