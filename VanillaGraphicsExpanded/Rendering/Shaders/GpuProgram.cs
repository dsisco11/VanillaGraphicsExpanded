using System;
using System.Collections.Generic;
using System.Threading;
using System.Linq;

using VanillaGraphicsExpanded.Numerics;
using VanillaGraphicsExpanded.PBR;
using VanillaGraphicsExpanded.Rendering;
using StageShader = VanillaGraphicsExpanded.Rendering.Shaders.Stages.Shader;
using VanillaGraphicsExpanded.Rendering.Shaders.Stages;

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
/// - Load built SPIR-V variants; retain source processing for diagnostics
/// - Apply a GL debug label to the linked program
///
/// NOTE: When VGE shader programs inline imports themselves, the Harmony import hook should
/// skip these programs to avoid double-processing (deferred decision).
/// </summary>
public abstract partial class GpuProgram : ShaderProgram
{
    #region Fields

    private readonly StageShader vertexStage;
    private readonly StageShader fragmentStage;
    private readonly StageShader geometryStage;

    private readonly Dictionary<string, int> uniformLocationCache = new(StringComparer.Ordinal);
    private int uniformLocationCacheProgramId;

    private readonly HashSet<string> warnedMissingUniforms = new(StringComparer.Ordinal);
    private int warnedMissingUniformsProgramId;

    private readonly HashSet<string> warnedNotBound = new(StringComparer.Ordinal);
    private int warnedNotBoundProgramId;

    private GpuProgramLayout? programLayout;

    private ICoreClientAPI? capi;
    private ILogger? log;

    private int recompileQueued;

    #endregion

    #region Layout

    protected virtual GpuProgramLayout CreateLayout() => new();

    internal GpuProgramLayout ProgramLayout => programLayout ??= CreateLayout();

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

    private bool IsUniformActive(string uniformName, bool warnIfMissing)
    {
        int loc = GetUniformLocationOrArray0(uniformName);
        if (loc >= 0)
        {
            return true;
        }

        if (!warnIfMissing || log is null)
        {
            return false;
        }

        if (warnedMissingUniformsProgramId != ProgramId)
        {
            warnedMissingUniformsProgramId = ProgramId;
            warnedMissingUniforms.Clear();
        }

        if (warnedMissingUniforms.Add(uniformName))
        {
            log.Warning($"[VGE][{ShaderName}] Uniform '{uniformName}' is inactive/optimized-away; skipping GL bind.");
        }

        return false;
    }

    #endregion

    #region Texture Binding (Sampler-Aware)

    protected void BindTexture2D(string uniformName, GpuTexture? texture, int unit)
    {
        bool hasContract = ProgramLayout.TryGetContractSamplerSpec(uniformName, out int contractUnit, out bool contractRequired);
        if (!hasContract)
        {
            // Legacy behavior: bind unit is caller-driven, set uniform every time.
            if (!IsUniformActive(uniformName, warnIfMissing: false))
            {
                return;
            }

            SetSamplerUnitLegacy(uniformName, unit);
            contractUnit = unit;
        }

        // Contract path: don't bind/unbind if the sampler uniform is optimized away.
        if (hasContract && !IsUniformActive(uniformName, warnIfMissing: contractRequired))
        {
            return;
        }

        if (texture is null)
        {
            GlStateCache.Current.BindTexture(TextureTarget.Texture2D, contractUnit, 0, sampler: null);
            return;
        }

        texture.Bind(contractUnit);
    }

    protected void BindTexture3D(string uniformName, GpuTexture? texture, int unit)
    {
        bool hasContract = ProgramLayout.TryGetContractSamplerSpec(uniformName, out int contractUnit, out bool contractRequired);
        if (!hasContract)
        {
            if (!IsUniformActive(uniformName, warnIfMissing: false))
            {
                return;
            }

            SetSamplerUnitLegacy(uniformName, unit);
            contractUnit = unit;
        }

        if (hasContract && !IsUniformActive(uniformName, warnIfMissing: contractRequired))
        {
            return;
        }

        if (texture is null)
        {
            GlStateCache.Current.BindTexture(TextureTarget.Texture3D, contractUnit, 0, sampler: null);
            return;
        }

        texture.Bind(contractUnit);
    }

    protected void BindExternalTexture2D(string uniformName, int textureId, int unit, GpuSampler sampler)
    {
        bool hasContract = ProgramLayout.TryGetContractSamplerSpec(uniformName, out int contractUnit, out bool contractRequired);
        if (!hasContract)
        {
            if (!IsUniformActive(uniformName, warnIfMissing: false))
            {
                return;
            }

            SetSamplerUnitLegacy(uniformName, unit);
            contractUnit = unit;
        }

        if (hasContract && !IsUniformActive(uniformName, warnIfMissing: contractRequired))
        {
            return;
        }

        GlStateCache.Current.BindTexture(TextureTarget.Texture2D, contractUnit, textureId, sampler);
    }

    protected void BindExternalTexture3D(string uniformName, int textureId, int unit, GpuSampler sampler)
    {
        bool hasContract = ProgramLayout.TryGetContractSamplerSpec(uniformName, out int contractUnit, out bool contractRequired);
        if (!hasContract)
        {
            if (!IsUniformActive(uniformName, warnIfMissing: false))
            {
                return;
            }

            SetSamplerUnitLegacy(uniformName, unit);
            contractUnit = unit;
        }

        if (hasContract && !IsUniformActive(uniformName, warnIfMissing: contractRequired))
        {
            return;
        }

        GlStateCache.Current.BindTexture(TextureTarget.Texture3D, contractUnit, textureId, sampler);
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
    /// Call from the program's Register method to enable define-triggered recompiles.
    /// </summary>
    public void Initialize(ICoreClientAPI api, ILogger? logger = null)
    {
        capi = api ?? throw new ArgumentNullException(nameof(api));
        log = logger ?? api.Logger;

        // Stage ownership is kept here, but stage behavior is fully encapsulated.
        // These delegates are safe because they are only invoked after Initialize() (when capi is set).
        // Vertex/Fragment/Geometry slots are provided by the engine ShaderProgram base.

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

    #region Legacy Sampler Unit Assignment

    private void SetSamplerUnitLegacy(string uniformName, int unit)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(uniformName);

        if (!EnsureProgramIsBound(operationKey: $"samplerunit:{uniformName}"))
        {
            return;
        }

        int loc = GetUniformLocationOrArray0(uniformName);
        if (loc < 0)
        {
            return;
        }

        try
        {
            GL.Uniform1(loc, unit);
        }
        catch (Exception ex)
        {
            log?.Warning($"[VGE][{ShaderName}] Failed to set sampler uniform '{uniformName}' to unit {unit}: {ex.Message}");
        }
    }

    private bool EnsureProgramIsBound(string operationKey)
    {
        if (ProgramId == 0)
        {
            return false;
        }

        int currentProgram;
        bool hasCachedProgram = GlStateCache.Current.TryGetCachedCurrentProgram(out currentProgram);

        if (currentProgram == ProgramId)
        {
            return true;
        }

        var logger = log;
        if (logger is null)
        {
            return false;
        }

        if (warnedNotBoundProgramId != ProgramId)
        {
            warnedNotBoundProgramId = ProgramId;
            warnedNotBound.Clear();
        }

        if (warnedNotBound.Add(operationKey))
        {
            if (!hasCachedProgram)
            {
                logger.Error($"[VGE][{ShaderName}] Attempted to set program state, but current program is unknown (state cache not primed). Ensure binds go through the PSO/state-cache and call Use()/UseScope() before setting program state.");
            }
            else
            {
                logger.Error($"[VGE][{ShaderName}] Attempted to set program state while it is not bound (expected {ProgramId}, current {currentProgram}). Call Use()/UseScope() first.");
            }
        }

        return false;
    }

    #endregion

    #region Program Binding

    /// <summary>
    /// Binds this program (using the engine's <see cref="ShaderProgram.Use"/>), returning a scope that
    /// restores the previous program binding when disposed.
    /// </summary>
    public ProgramUseScope UseScope()
    {
        var previous = ShaderProgramBase.CurrentShaderProgram;
        int previousId = previous?.ProgramId ?? 0;
        if (previous is null)
            GlStateCache.Current.TryGetCachedCurrentProgram(out previousId);
        if (ReferenceEquals(previous, this))
            return default; // A nested use borrows the outer scope's activation.

        try
        {
            // The engine rejects overlapping shader owners, even when GL allows a bind.
            previous?.Stop();
            GlStateCache.Current.NotifyProgramBound(0);
            Use();
            GlStateCache.Current.NotifyProgramBound(ProgramId);
            return new ProgramUseScope(previous, previousId, this);
        }
        catch
        {
            // Restore the caller's ownership if activation failed during shutdown/reload.
            if (previous is not null) previous.Use();
            else GlStateCache.Current.UseProgram(previousId);
            GlStateCache.Current.NotifyProgramBound(previousId);
            return default;
        }
    }
    /// <summary>
    /// Attempts to bind this program.
    /// </summary>
    public bool TryUse()
    {
        if (ProgramId == 0)
        {
            return false;
        }

        try
        {
            Use();
            GlStateCache.Current.NotifyProgramBound(ProgramId);
            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Unbinds any program (binds program 0).
    /// </summary>
    public static void Unuse()
    {
        GlStateCache.Current.UnbindProgram();
    }

    /// <summary>
    /// Scope that restores the previous program binding when disposed.
    /// </summary>
    public readonly struct ProgramUseScope : IDisposable
    {
        private readonly ShaderProgramBase? previous;
        private readonly int previousProgramId;
        private readonly GpuProgram? current;

        /// <summary>Remembers both the engine owner and any engine-independent GL binding.</summary>
        internal ProgramUseScope(ShaderProgramBase? previous, int previousProgramId, GpuProgram current)
        {
            this.previous = previous;
            this.previousProgramId = previousProgramId;
            this.current = current;
        }

        /// <summary>Stops this activation and restores the caller's engine and GL state together.</summary>
        public void Dispose()
        {
            if (current is null) return;
            current.Stop();
            GlStateCache.Current.NotifyProgramBound(0);
            if (previous is not null) previous.Use();
            else GlStateCache.Current.UseProgram(previousProgramId);
            GlStateCache.Current.NotifyProgramBound(previousProgramId);
        }
    }
    #endregion

    /// <summary>
    /// Returns the cached uniform location for <paramref name="uniformName"/>.
    /// When <paramref name="uniformName"/> refers to an array, this method also tries <c>name[0]</c>,
    /// since different compilers expose either the base name or the explicit element name.
    /// </summary>
    protected int GetUniformLocationOrArray0(string uniformName)
    {
        if (ProgramId == 0)
        {
            return -1;
        }

        if (uniformLocationCacheProgramId != ProgramId)
        {
            uniformLocationCache.Clear();
            uniformLocationCacheProgramId = ProgramId;
        }

        if (uniformLocationCache.TryGetValue(uniformName, out int cached))
        {
            return cached;
        }

        // Spec allows querying the base name OR the [0] name.
        // Some compilers only expose one of these names.
        int loc = ProgramLayout.GetUniformLocation(ProgramId, uniformName);
        if (loc < 0)
        {
            loc = ProgramLayout.GetUniformLocation(ProgramId, $"{uniformName}[0]");
        }

        uniformLocationCache[uniformName] = loc;
        return loc;
    }

    #region Compilation

    /// <summary>
    /// Loads built shader variants, specializes settings, and links their numeric interfaces.
    /// Intended for memory shader programs.
    /// </summary>
    public bool CompileAndLink()
    {
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

            // Diagnostics consume the same captured settings. Binary selection never rereads mutable state.
            var diagnostics = plan.Settings.Values.ToDictionary(p => p.Key, p => (string?)p.Value.Canonical);
            foreach (var selection in plan.Stages)
            {
                var stage = selection.Stage;
                StageShader? source = stage.Kind switch
                {
                    Contracts.ShaderStageKind.Vertex => vertexStage,
                    Contracts.ShaderStageKind.Fragment => fragmentStage,
                    Contracts.ShaderStageKind.Geometry => geometryStage,
                    _ => null
                };
                source?.LoadAndApply(capi, System.IO.Path.ChangeExtension(stage.Source, null), diagnostics, log);
            }
            bool ok = CompileSpirv(plan);
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
            log?.Error($"[VGE][{ShaderName}] Exception during CompileAndLink(): {ex}");
            return false;
        }
    }

    #endregion

    #region Recompile Scheduling

    /// <summary>Coalesces requests on the render thread and skips unchanged or disposed generations.</summary>
    protected virtual void RequestRecompile()
    {
        var api = capi;
        if (api is null)
        {
            return;
        }

        // Collapse setting writes into one callback; compare the final effective plan before preparing GPU objects.
        if (Interlocked.Exchange(ref recompileQueued, 1) != 0)
        {
            return;
        }

        api.Event.EnqueueMainThreadTask(
            () =>
            {
                Interlocked.Exchange(ref recompileQueued, 0);

                try
                {
                    if (Disposed) return;
                    bool unchanged;
                    lock (settingsLock) unchanged = !Disposed && ProgramId != 0 && installedPlan != null && RequestedPlan.SameInputs(installedPlan);
                    if (!unchanged) CompileAndLink();
                }
                catch (Exception ex)
                {
                    log?.Error($"[VGE] Shader recompile failed for '{ShaderName}': {ex}");
                }
            },
            $"vge:recompile:{ShaderName}");
    }

    #endregion

    #region Construction

    protected GpuProgram()
    {
        vertexStage = new StageShader(
            stageExtension: "vsh",
            engineShaderType: EnumShaderType.VertexShader,
            getSlot: () => VertexShader,
            setSlot: s => VertexShader = (global::Vintagestory.Client.NoObf.Shader)s);

        fragmentStage = new StageShader(
            stageExtension: "fsh",
            engineShaderType: EnumShaderType.FragmentShader,
            getSlot: () => FragmentShader,
            setSlot: s => FragmentShader = (global::Vintagestory.Client.NoObf.Shader)s);

        geometryStage = new StageShader(
            stageExtension: "gsh",
            engineShaderType: EnumShaderType.GeometryShader,
            getSlot: () => GeometryShader,
            setSlot: s => GeometryShader = (global::Vintagestory.Client.NoObf.Shader)s);
    }

    #endregion
}
