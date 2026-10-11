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
public abstract partial class GpuProgram : IDisposable, IGpuProgram
{
    #region Fields

    private GpuProgramLayout? programLayout;
    private GpuProgramObject? executable;

    /// <summary>Gets the installed native executable without transferring ownership.</summary>
    public int ProgramId => executable?.ProgramId ?? 0;
    /// <summary>Names this declaration for lookup and diagnostics.</summary>
    public string PassName { get; set; } = string.Empty;
    /// <summary>Selects the built asset domain.</summary>
    public string AssetDomain { get; set; } = string.Empty;

    private ICoreClientAPI? capi;
    private ILogger? log;



    #endregion

    #region Layout

    protected virtual GpuProgramLayout CreateLayout() => new();

    internal GpuProgramLayout ProgramLayout => programLayout ??= CreateLayout();
    /// <summary>Exposes the installed layout at the shared submission boundary.</summary>
    GpuProgramLayout IShaderSubmissionTarget.ProgramLayout => ProgramLayout;
    /// <summary>Exposes the installed executable at the shared submission boundary.</summary>
    int IShaderSubmissionTarget.ProgramId => ProgramId;

    #endregion

    #region Properties and Hooks

    /// <summary>
    /// Returns <c>true</c> when this program has a non-zero <see cref="ProgramId"/>.
    /// </summary>
    public bool IsLinked => ProgramId != 0 && !lifetime.IsRetired;

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

    }

    #endregion


    #region Program Binding

    /// <summary>Publishes inputs and restores the enclosing owner's inputs on scope exit.</summary>
    public ProgramUseScope UseScope() => (ProgramUseScope)((IGpuProgram)this).BeginUse();
    /// <summary>Lets complete boundaries restore resource snapshots without redundant publication.</summary>
    internal ProgramUseScope UseScope(bool replayInputs) => (ProgramUseScope)((IGpuProgram)this).BeginUse(replayInputs);
    /// <summary>Captures restoration in the existing context-aware program tracker.</summary>
    IDisposable IGpuProgram.OpenUseScope(bool replayInputs) => new ProgramUseScope(StateCache.Current.CaptureProgramScope(replayInputs));
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

    /// <summary>Shares one exactly-once restoration token between scope copies.</summary>
    public readonly struct ProgramUseScope : IDisposable
    {
        private readonly IDisposable? token;
        /// <summary>Wraps the existing program tracker token.</summary>
        internal ProgramUseScope(IDisposable token) => this.token = token;
        /// <summary>Restores through the originating tracked lifetime.</summary>
        public void Dispose() => token?.Dispose();
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
        if (lifetime.IsRetired) return false;
        lifetime.RequireUnborrowed();
        if (capi is null)
        {
            throw new InvalidOperationException("GpuProgram was not initialized. Call Initialize(api) first.");
        }

        try
        {
            Contracts.ShaderLoadPlan plan;
            ulong assets;
            lock (settingsLock) { plan = RequestedPlan; assets = assetGeneration; }
            if (plan.Stages.Any(s => s.Stage.Kind == Contracts.ShaderStageKind.Compute))
                throw new InvalidOperationException("A graphics program cannot load a compute contract.");

            // The captured plan supplies binary selection and bindings without reconstructing GLSL.
            bool ok = CompileSpirv(plan, assets);
            if (ok)
            {
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
            PreparationFailure = ex;
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
                return !lifetime.IsRetired && (ProgramId == 0 || installedPlan == null || !RequestedPlan.SameInputs(installedPlan));
        }
    }
    #endregion

}
