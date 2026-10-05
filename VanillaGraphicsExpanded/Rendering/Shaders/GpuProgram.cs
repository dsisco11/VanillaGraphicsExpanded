using System;
using System.Collections.Generic;

using System.Linq;

using VanillaGraphicsExpanded.Numerics;
using VanillaGraphicsExpanded.PBR;
using VanillaGraphicsExpanded.Rendering;

using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Vintagestory.Client.NoObf;

using OpenTK.Graphics.OpenGL;

namespace VanillaGraphicsExpanded.Rendering.Shaders;

/// <summary>
/// Base class for all VGE-owned shader programs.
///
/// Responsibilities:
/// - Own typed requested/installed settings; <see cref="SetDefine"/> adapts declared names and aliases
/// - Load built SPIR-V variants using compiled binding contracts
/// - Apply a GL debug label to the linked program
///
/// </summary>
public abstract partial class GpuProgram : ShaderProgram, IShaderProgram, IDisposable, IShaderSubmissionTarget
{
    #region Fields

    private GpuProgramLayout? programLayout;

    private ICoreClientAPI? capi;
    private ILogger? log;



    #endregion

    #region Layout

    protected virtual GpuProgramLayout CreateLayout() => new();

    internal GpuProgramLayout ProgramLayout => programLayout ??= CreateLayout();
    /// <summary>Exposes the installed layout at the shared submission boundary.</summary>
    GpuProgramLayout IShaderSubmissionTarget.ProgramLayout => ProgramLayout;
    /// <summary>Exposes the engine executable at the shared submission boundary.</summary>
    int IShaderSubmissionTarget.ProgramId => ProgramId;

    #endregion

    #region Properties and Hooks

    /// <summary>
    /// Returns <c>true</c> when this program has a non-zero <see cref="ShaderProgram.ProgramId"/>.
    /// </summary>
    public bool IsLinked => ProgramId != 0 && !Disposed;

    /// <summary>
    /// Program identity used for GL debug labels and explicit catalog lookup by generic owners.
    /// </summary>
    protected string ShaderName => PassName;

    /// <summary>Supplies the shader declaration; generic fixture owners may resolve an explicit catalog identity.</summary>
    internal virtual Contracts.GpuShaderContract ProgramContract => Contracts.GpuShaderContracts.Registry.FindProgram(ShaderName);

    /// <summary>
    /// Optional hook for derived programs that need to refresh caches after (re)compile.
    /// </summary>
    protected virtual void OnAfterCompile() { }

    /// <summary>
    /// Optional warning sink for layout/contract guardrails.
    /// </summary>
    protected Action<string>? LayoutWarn
        => log is null ? null : msg => log.Warning($"[VGE][{ShaderName}] {msg}");

    #endregion

    #region Uniform Block Binding (UBO)

    /// <summary>
    /// Binds a UBO to the named uniform block for this program.
    /// </summary>
    internal bool TryBindUniformBlock(string blockName, GpuUniformBuffer buffer)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(blockName);
        ArgumentNullException.ThrowIfNull(buffer);

        return ProgramLayout.TryBindUniformBlock(ProgramId, blockName, buffer, msg => log?.Warning($"[VGE][{ShaderName}] {msg}"));
    }


    #endregion


    #region Program Layout

    /// <summary>
    /// Gets cached binding-related resources for the currently linked program.
    /// Updated after successful <see cref="CompileAndLink"/>.
    /// </summary>
    internal GpuProgramLayout ResourceBindings => ProgramLayout;

    #endregion

    #region Initialization and Defines

    /// <summary>
    /// Associates a declaration with its application without reading binaries or creating an executable.
    /// </summary>
    public void Initialize(ICoreClientAPI api, ILogger? logger = null)
    {
        capi = api ?? throw new ArgumentNullException(nameof(api));
        log = logger ?? api.Logger;

        // Keep AssetDomain aligned with the import system default unless a derived program explicitly overrides it.
        if (string.IsNullOrWhiteSpace(AssetDomain))
        {
            AssetDomain = ShaderImportsSystem.DefaultDomain;
        }

        // IMPORTANT: Memory shader programs must provide stage instances themselves.
        // The engine may attempt to compile/validate registered programs and will log "shader missing" (and may NRE)
        // if these slots are null.
        VertexShader ??= (global::Vintagestory.Client.NoObf.Shader)api.Shader.NewShader(EnumShaderType.VertexShader);
        FragmentShader ??= (global::Vintagestory.Client.NoObf.Shader)api.Shader.NewShader(EnumShaderType.FragmentShader);
    }

    #endregion


    #region Program Binding

    /// <summary>
    /// Binds this program (using the engine's <see cref="ShaderProgram.Use"/>), returning a scope that
    /// restores the previous program binding when disposed.
    /// </summary>
    public ProgramUseScope UseScope()
    {
        RequireOutsideSubmission();
        try
        {
            if (!EnsureReady()) throw new InvalidOperationException("Shader preparation failed.");
        }
        catch
        {
            ClearFailedActivation();
            throw;
        }
        var previous = ShaderProgramBase.CurrentShaderProgram;
        int previousId = previous?.ProgramId ?? 0;
        if (previous is null)
            previousId = StateCache.Current.GetCurrentProgram();
        // Capture ownership now; nested disposal must fail restoration rather than bind a retired GL name.
        var previousCompute = previous is null ? GpuComputeShader.FindOwner(previousId) : null;

        try
        {
            // The engine rejects overlapping shader owners, even when GL allows a bind.
            if (!ReferenceEquals(previous, this))
            {
                previous?.Stop();
                StateCache.Current.NotifyProgramBound(0);
            }
            Use();
            StateCache.Current.NotifyProgramBound(ProgramId);
            return new ProgramUseScope(previous, previousId, previousCompute, this);
        }
        catch (Exception activationFailure)
        {
            // Restore the caller's ownership if activation failed during shutdown/reload.
            try { RestoreProgram(previous, previousId, previousCompute); }
            catch (Exception restorationFailure) { throw new AggregateException(activationFailure, restorationFailure); }
            throw;
        }
    }
    /// <summary>
    /// Attempts to bind this program.
    /// </summary>
    public bool TryUse()
    {
        try { Use(); return true; }
        catch { return false; }
    }
    /// <summary>
    /// Unbinds any program (binds program 0).
    /// </summary>
    public static void Unuse()
    {
        StateCache.Current.UnbindProgram();
    }

    /// <summary>Restores owned resources through submission and preserves foreign engine activation policy.</summary>
    private static void RestoreProgram(ShaderProgramBase? previous, int previousId, GpuComputeShader? previousCompute)
    {
        try
        {
            if (previous is GpuProgram owner) owner.Use();
            else if (previous is not null) previous.Use();
            else if (previousCompute is not null) previousCompute.Use();
            else StateCache.Current.UseProgram(previousId);
            StateCache.Current.NotifyProgramBound(previous?.ProgramId ?? previousCompute?.ProgramId ?? previousId);
        }
        catch (Exception restorationFailure)
        {
            try { StateCache.Current.UnbindProgram(); }
            catch (Exception unbindFailure)
            {
                throw new ShaderOwnershipRestoreException(new AggregateException(restorationFailure, unbindFailure));
            }
            finally { StateCache.Current.Invalidate(EPipelineState.Program); }
            throw new ShaderOwnershipRestoreException(restorationFailure);
        }
    }

    /// <summary>
    /// Scope that restores the previous program binding when disposed.
    /// </summary>
    public readonly struct ProgramUseScope : IDisposable
    {
        private readonly ShaderProgramBase? previous;
        private readonly int previousProgramId;
        private readonly GpuComputeShader? previousCompute;
        private readonly GpuProgram? current;

        /// <summary>Remembers both the engine owner and any engine-independent GL binding.</summary>
        internal ProgramUseScope(ShaderProgramBase? previous, int previousProgramId, GpuComputeShader? previousCompute, GpuProgram current)
        {
            this.previous = previous;
            this.previousProgramId = previousProgramId;
            this.previousCompute = previousCompute;
            this.current = current;
        }

        /// <summary>Stops this activation and restores the caller's engine and GL state together.</summary>
        public void Dispose()
        {
            if (current is null) return;
            if (!ReferenceEquals(current, previous))
            {
                current.Stop();
                StateCache.Current.NotifyProgramBound(0);
            }
            RestoreProgram(previous, previousProgramId, previousCompute);
        }
    }
    #endregion


    #region Compilation

    /// <summary>
    /// Loads built shader variants, specializes settings, and links their numeric interfaces.
    /// Intended for memory shader programs.
    /// </summary>
    public bool CompileAndLink()
    {
        RequireOutsideSubmission();
        if (retired) return false;
        if (capi is null)
        {
            throw new InvalidOperationException("GpuProgram was not initialized. Call Initialize(api) first.");
        }

        try
        {
            Contracts.ShaderLoadPlan plan;
            lock (settingsLock) plan = RequestedPlan;
            if (plan.Stages.Any(s => s.Stage.Kind == Contracts.ShaderStageKind.Compute))
                throw new InvalidOperationException("A graphics program cannot load a compute contract.");

            // The captured plan supplies binary selection and bindings without reconstructing GLSL.
            if (Disposed) registeredWithEngine = false;
            bool ok = CompileSpirv(plan);
            if (ok)
            {
                CompletePreparation();
                GlDebug.TryLabel(ObjectLabelIdentifier.Program, ProgramId, ShaderName);
                // Owner notifications run after installation and cannot turn a committed link into a failed candidate.
                try { OnAfterCompile(); }
                catch (Exception ex) { log?.Error($"[VGE][{ShaderName}] Post-link notification failed: {ex}"); }
            }
            return ok;
        }
        catch (Exception ex)
        {
            // Never let shader compilation exceptions bring down the client.
            log?.Error($"[VGE][{ShaderName}] Exception during CompileAndLink(): {ex}");
            return false;
        }
    }

    #endregion

    #region Recompile Scheduling

    /// <summary>Notifies derived owners that settings changed; the default leaves preparation to the consumer.</summary>
    protected virtual void RequestRecompile()
    {
        // Settings stay pending until explicit preparation or activation.
    }

    /// <summary>Checks whether a live owner still needs its latest effective settings installed.</summary>
    internal bool NeedsRecompile
    {
        get
        {
            lock (settingsLock)
                return !Disposed && (ProgramId == 0 || installedPlan == null || !RequestedPlan.SameInputs(installedPlan));
        }
    }
    #endregion

}
