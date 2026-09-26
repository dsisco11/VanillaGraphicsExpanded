using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;

using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.Rendering.Contracts;
using VanillaGraphicsExpanded.Rendering.ProgramBinaries;
using VanillaGraphicsExpanded.Rendering.ShaderCompilation;

using Vintagestory.API.Common;

namespace VanillaGraphicsExpanded.Rendering;

/// <summary>
/// RAII wrapper around an OpenGL compute program (a linked program containing a compute shader stage).
/// Deletion is deferred to <see cref="GpuResourceManager"/> when available.
/// All methods require a current GL context on the calling thread.
/// </summary>
internal sealed partial class GpuComputePipeline : GpuResource, IDisposable
{
    private int programId;
    private GpuProgramLayout programLayout = GpuProgramLayout.Empty;

    private Action<string>? warn;
    /// <summary>The coherent settings used to create this linked compute executable.</summary>
    internal ShaderSettings? InstalledSettings { get; private set; }

    protected override nint ResourceId
    {
        get => programId;
        set => programId = (int)value;
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

    /// <summary>
    /// Returns <c>true</c> when the program has a non-zero id and has not been disposed.
    /// </summary>
    public new bool IsValid => programId != 0 && !IsDisposed;

    private GpuComputePipeline(int programId, GpuProgramLayout programLayout, Action<string>? warn)
    {
        this.programId = programId;
        this.programLayout = programLayout;
        this.warn = warn;
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
#if DEBUG
        using var errors = new GlDebug.ErrorScope($"{debugName}: compute linking and bindings");
#endif
        try
        {
            programId = ShaderProgramLink.Submit([computeShader.ShaderId], retrievableBinary, out _);
#if DEBUG
            GlDebug.TryLabel(ObjectLabelIdentifier.Program, programId, debugName);
            GlDebug.ThrowIfErrors($"{debugName}: compute create/attach/link program={programId}");
#endif
            bool linked = ShaderProgramLink.Validate(programId, out infoLog);
            GL.DetachShader(programId, computeShader.ShaderId);

            if (disposeShaderAfterLink)
            {
                computeShader.Dispose();
            }

            if (!linked)
            {
                try { GL.DeleteProgram(programId); } catch { }
                return false;
            }

            pipeline = PrepareLinkedPipeline(programId, computeShader.BindingContract, layout, warn, debugName);
            return true;
        }
        catch (Exception ex)
        {
            infoLog = (infoLog.Length > 0 ? infoLog + "\n" : string.Empty) + ex.Message;

            try
            {
                if (programId != 0)
                {
                    GL.DeleteProgram(programId);
                }
            }
            catch
            {
            }

            pipeline = null;
            return false;
        }
        finally
        {
            // Cover failures before detachment/link status as well as ordinary successful ownership transfer.
            if (disposeShaderAfterLink) computeShader.Dispose();
        }
    }

    #region Linked interface preparation
    /// <summary>Prepares bindings for both normal links and cached executables before ownership transfer.</summary>
    private static GpuComputePipeline PrepareLinkedPipeline(int program, GpuBindingContract? contract,
        GpuProgramLayout? layout, Action<string>? warn, string? debugName)
    {
        // Prepare a separate layout so failure cannot mutate the caller's installed interface.
        var prepared = layout?.CreateCandidate() ?? new GpuProgramLayout();
        if (contract != null) prepared.BinaryInterface = new Spirv.GpuProgramInterface(program, [contract]);
        prepared.ApplyContract(program, warn);
#if DEBUG
        prepared.ValidateContract(program, warn);
#endif
        var pipeline = new GpuComputePipeline(program, prepared, warn);
        pipeline.SetDebugName(debugName);
        return pipeline;
    }
    #endregion

    private static int spirvUsedCount;

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
        Func<Spirv.ShaderBinaryDigest.Manifest?> digestIndex, IAssetManager? assets = null)
    {
        pipeline = null;
        infoLog = string.Empty;
        try
        {
            var plan = new ShaderLoadPlan(settings);
            if (plan.Stages.Count != 1 || plan.Stages[0].Stage.Kind != ShaderStageKind.Compute)
                throw new ArgumentException($"Program '{settings.Contract.Identity}' is not a compute program.");
            using var pending = assets == null ? null : ShaderLinkBatch.Take(assets, PBR.ShaderImportsSystem.DefaultDomain, plan);
            var inputs = pending?.Inputs ?? DriverProgramCache.Prepare(plan, read, digestIndex);
            int candidate = pending?.DetachProgram() ?? GL.CreateProgram();
            try
            {
                if (pending != null || DriverProgramCache.TryLoad(candidate, inputs))
                {
                    pipeline = PrepareLinkedPipeline(candidate, plan.Stages[0].Stage.Bindings, layout, warn, debugName);
                    candidate = 0;
                    if (pending != null)
                    {
                        foreach (var stage in pending.Stages) GL.DetachShader(pipeline.ProgramId, stage.Shader);
                        if (!pending.Cached) DriverProgramCache.Save(pipeline.ProgramId, inputs);
                    }
                }
                else
                {
                    // Discard a rejected executable before returning to the established linking owner.
                    GL.DeleteProgram(candidate);
                    candidate = 0;
                    var loaded = Spirv.SpirvStageLoader.Load(plan.Stages[0], inputs.Read);
                    using var module = new GpuShaderModule(loaded.Shader, ShaderType.ComputeShader) { BindingContract = loaded.Contract };
                    if (!TryCreate(module, out pipeline, out infoLog, debugName, layout: layout, warn: warn,
                        retrievableBinary: inputs.Key != null)) return false;
                    DriverProgramCache.Save(pipeline!.ProgramId, inputs);
                }
                pipeline!.InstalledSettings = settings;
                Interlocked.Increment(ref spirvUsedCount);
                return true;
            }
            finally { if (candidate != 0) GL.DeleteProgram(candidate); }
        }
        catch (Exception ex)
        {
            pipeline?.Dispose();
            pipeline = null;
            infoLog = ex.Message;
            return false;
        }
    }

    /// <summary>Loads a typed, explicitly declared compute program from packaged assets.</summary>
    public static bool TryCreateFromAssets(ICoreAPI api, ShaderSettings settings,
        out GpuComputePipeline? pipeline, out string infoLog, string? debugName = null,
        ILogger? log = null, CancellationToken ct = default, GpuProgramLayout? layout = null)
    {
        ct.ThrowIfCancellationRequested();
        ReadOnlySpan<byte> Read(string path) => api.Assets.TryGet(AssetLocation.Create("shaders/" + path,
            PBR.ShaderImportsSystem.DefaultDomain), loadAsset: true)?.Data
            ?? throw new InvalidOperationException("Missing built compute shader asset: " + path);
        return TryCreateFromBinaryContract(settings, Read, out pipeline, out infoLog,
            debugName, layout, message => (log ?? api.Logger)?.Warning(message),
            () => ShaderDigestIndexCache.ForAssets(api.Assets, PBR.ShaderImportsSystem.DefaultDomain), api.Assets);
    }
    /// <summary>
    /// Creates a compute pipeline from a required packaged SPIR-V binary. The legacy preference parameter is ignored; GLSL fallback is never used.
    /// </summary>
    /// <remarks>
    /// This is the recommended runtime entry point for VGE-owned compute shaders.
    /// </remarks>
    public static bool TryCreateFromAssets(
        ICoreAPI api,
        string shaderName,
        out GpuComputePipeline? pipeline,
        out global::VanillaGraphicsExpanded.ShaderSourceCode? sourceCode,
        out string infoLog,
        bool preferSpirv = true,
        string stageExtension = "csh",
        IReadOnlyDictionary<string, string?>? defines = null,
        string? debugName = null,
        ILogger? log = null,
        CancellationToken ct = default,
        GpuProgramLayout? layout = null)
    {
        ArgumentNullException.ThrowIfNull(api);
        ArgumentException.ThrowIfNullOrWhiteSpace(shaderName);
        ArgumentException.ThrowIfNullOrWhiteSpace(stageExtension);

        pipeline = null;
        sourceCode = null;
        infoLog = string.Empty;

        ct.ThrowIfCancellationRequested();
        try
        {
            if (stageExtension != "csh") throw new ArgumentException("Compute programs require a compute stage.");
            var settings = new ShaderSettings(GpuShaderContracts.Registry.FindProgram(shaderName), defines);
            return TryCreateFromAssets(api, settings, out pipeline, out infoLog, debugName, log, ct, layout);
        }
        catch (Exception ex) { infoLog = ex.Message; return false; }
    }
    [Obsolete("Use TryCreateFromAssets(..., preferSpirv: true/false, ...) instead.")]
    public static bool TryCreateFromAssetsPreferSpirv(
        ICoreAPI api,
        string shaderName,
        out GpuComputePipeline? pipeline,
        out global::VanillaGraphicsExpanded.ShaderSourceCode? sourceCode,
        out string infoLog,
        string stageExtension = "csh",
        IReadOnlyDictionary<string, string?>? defines = null,
        string? debugName = null,
        ILogger? log = null,
        CancellationToken ct = default)
        => TryCreateFromAssets(
            api: api,
            shaderName: shaderName,
            pipeline: out pipeline,
            sourceCode: out sourceCode,
            infoLog: out infoLog,
            preferSpirv: true,
            stageExtension: stageExtension,
            defines: defines,
            debugName: debugName,
            log: log,
            ct: ct);
    /// <summary>
    /// Uses this compute pipeline via <c>glUseProgram</c>.
    /// </summary>
    public void Use()
    {
        if (!EnsureReady())
        {
            throw new InvalidOperationException("Compute preparation failed: " + preparationLog);
        }

        GlStateCache.Current.UseProgram(programId);
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

        GlStateCache.Current.UseProgram(programId);
        return true;
    }

    /// <summary>
    /// Uses this pipeline and returns a scope that restores the previous program when disposed.
    /// </summary>
    public ProgramScope UseScope()
    {
        if (!EnsureReady()) throw new InvalidOperationException(preparationLog);
        var scope = GlStateCache.Current.UseProgramScope(programId);
        return new ProgramScope(scope);
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
        private readonly GlStateCache.ProgramScope scope;

        public ProgramScope(GlStateCache.ProgramScope scope)
        {
            this.scope = scope;
        }

        public void Dispose()
        {
            scope.Dispose();
        }
    }

    protected override void OnAfterDelete()
    {
        warn = null;

        // Keep the contract but clear the active snapshot for safety.
        programLayout.RebuildCache(programId: 0);
    }
}
