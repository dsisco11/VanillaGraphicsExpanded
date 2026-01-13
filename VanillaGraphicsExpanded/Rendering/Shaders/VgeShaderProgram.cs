using System;
using System.Collections.Generic;
using System.Threading;

using VanillaGraphicsExpanded.PBR;
using VanillaGraphicsExpanded.Rendering;
using StageShader = VanillaGraphicsExpanded.Rendering.Shaders.Stages.Shader;
using VanillaGraphicsExpanded.Rendering.Shaders.Stages;

using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.Client.NoObf;

namespace VanillaGraphicsExpanded.Rendering.Shaders;

/// <summary>
/// Base class for all VGE-owned shader programs.
///
/// Responsibilities:
/// - Own a runtime define-map; <see cref="SetDefine"/> triggers a recompile only on changes
/// - Compile from asset sources using <see cref="ShaderSourceCode"/> (imports handled inline)
/// - Apply a GL debug label to the linked program
///
/// NOTE: When VGE shader programs inline imports themselves, the Harmony import hook should
/// skip these programs to avoid double-processing (deferred decision).
/// </summary>
public abstract class VgeShaderProgram : ShaderProgram
{
    private readonly StageShader vertexStage;
    private readonly StageShader fragmentStage;
    private readonly StageShader geometryStage;

    private readonly Dictionary<string, string?> defines = new(StringComparer.Ordinal);
    private readonly object defineLock = new();

    private ICoreClientAPI? capi;
    private ILogger? log;

    private int recompileQueued;

    /// <summary>
    /// Shader name used for stage source naming and GL debug labels.
    /// Stage source names are always <c>{ShaderName}.vsh/.fsh/.gsh</c>.
    /// </summary>
    protected string ShaderName => PassName;

    /// <summary>
    /// Optional hook for derived programs that need to refresh caches after (re)compile.
    /// </summary>
    protected virtual void OnAfterCompile() { }

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

    /// <summary>
    /// Updates a define value and schedules a recompile if it changed.
    /// A null value means <c>#define NAME</c> (no explicit value).
    /// </summary>
    public bool SetDefine(string name, string? value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        bool changed;
        lock (defineLock)
        {
            changed = !defines.TryGetValue(name, out var existing) || existing != value;
            if (changed)
            {
                defines[name] = value;
            }
        }

        if (changed)
        {
            RequestRecompile();
        }

        return changed;
    }

    /// <summary>
    /// Removes a define and schedules a recompile if it existed.
    /// </summary>
    public bool RemoveDefine(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        bool changed;
        lock (defineLock)
        {
            changed = defines.Remove(name);
        }

        if (changed)
        {
            RequestRecompile();
        }

        return changed;
    }

    /// <summary>
    /// Compiles and links using VGE's source pipeline (imports + AST define injection).
    /// Intended for memory shader programs.
    /// </summary>
    public bool CompileAndLink()
    {
        if (capi is null)
        {
            throw new InvalidOperationException("VgeShaderProgram was not initialized. Call Initialize(api) first.");
        }

        try
        {
            // Preflight: avoid Vintage Story engine NREs by ensuring stage assets exist before we even attempt compilation.
            // This also provides a much clearer error message than the engine's generic "shader missing" logs.
            string domain = string.IsNullOrWhiteSpace(AssetDomain) ? ShaderImportsSystem.DefaultDomain : AssetDomain;

            string vshPath = $"shaders/{ShaderName}.vsh";
            string fshPath = $"shaders/{ShaderName}.fsh";

            bool hasVsh = capi.Assets.TryGet(AssetLocation.Create(vshPath, domain), loadAsset: true) is not null;
            bool hasFsh = capi.Assets.TryGet(AssetLocation.Create(fshPath, domain), loadAsset: true) is not null;

            if (!hasVsh || !hasFsh)
            {
                if (!hasVsh)
                {
                    log?.Error($"[VGE][{ShaderName}] Missing vertex shader asset: {domain}:{vshPath}");
                }
                if (!hasFsh)
                {
                    log?.Error($"[VGE][{ShaderName}] Missing fragment shader asset: {domain}:{fshPath}");
                }

                log?.Error($"[VGE][{ShaderName}] Shader assets were not found. The engine may crash if compilation proceeds; skipping Compile().");
                return false;
            }

            IReadOnlyDictionary<string, string?> defineSnapshot;
            lock (defineLock)
            {
                defineSnapshot = new Dictionary<string, string?>(defines, StringComparer.Ordinal);
            }

            vertexStage.LoadAndApply(capi, ShaderName, defineSnapshot, log);
            fragmentStage.LoadAndApply(capi, ShaderName, defineSnapshot, log);

            // Geometry stage is optional.
            geometryStage.TryLoadAndApplyOptional(capi, ShaderName, defineSnapshot, log);

            bool ok = Compile();
            if (ok)
            {
                GlDebug.TryLabel(OpenTK.Graphics.OpenGL.ObjectLabelIdentifier.Program, ProgramId, ShaderName);
                OnAfterCompile();
            }
            else
            {
                log?.Warning($"[VGE] Shader compile failed: {ShaderName}");
                LogDiagnostics();
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

    protected virtual void LogDiagnostics()
    {
        // Called only on compilation failures, on the render thread.
        // All OpenGL diagnostics are best-effort and must never throw.
        try
        {
            var v = vertexStage.CompileDiagnostics();
            var f = fragmentStage.CompileDiagnostics();
            var g = geometryStage.EmittedSource is null ? new StageShader.StageCompileDiagnostics { Success = true, InfoLog = string.Empty, ShaderId = 0 } : geometryStage.CompileDiagnostics();

            try
            {
                if (!string.IsNullOrWhiteSpace(v.InfoLog))
                {
                    log?.Error($"[VGE][{ShaderName}] Vertex shader info log:\n{v.InfoLog}");
                }
                if (!string.IsNullOrWhiteSpace(f.InfoLog))
                {
                    log?.Error($"[VGE][{ShaderName}] Fragment shader info log:\n{f.InfoLog}");
                }
                if (!string.IsNullOrWhiteSpace(g.InfoLog))
                {
                    log?.Error($"[VGE][{ShaderName}] Geometry shader info log:\n{g.InfoLog}");
                }

                var link = ShaderProgramDiagnostics.LinkDiagnosticsOnly(v.ShaderId, f.ShaderId, g.ShaderId);
                if (!string.IsNullOrWhiteSpace(link.ProgramInfoLog))
                {
                    log?.Error($"[VGE][{ShaderName}] Program link info log:\n{link.ProgramInfoLog}");
                }

                // Emit some context that will matter for future remapping.
                if (vertexStage.SourceCode?.ImportInlining.HadImports == true)
                {
                    log?.Notification($"[VGE][{ShaderName}] Vertex stage had imports; source-map available={vertexStage.SourceCode.ImportResult?.GetType().GetProperty("SourceMap")?.GetValue(vertexStage.SourceCode.ImportResult) is not null}");
                }
                if (fragmentStage.SourceCode?.ImportInlining.HadImports == true)
                {
                    log?.Notification($"[VGE][{ShaderName}] Fragment stage had imports; source-map available={fragmentStage.SourceCode.ImportResult?.GetType().GetProperty("SourceMap")?.GetValue(fragmentStage.SourceCode.ImportResult) is not null}");
                }
                if (geometryStage.SourceCode?.ImportInlining.HadImports == true)
                {
                    log?.Notification($"[VGE][{ShaderName}] Geometry stage had imports; source-map available={geometryStage.SourceCode.ImportResult?.GetType().GetProperty("SourceMap")?.GetValue(geometryStage.SourceCode.ImportResult) is not null}");
                }
            }
            finally
            {
                StageShader.DeleteDiagnosticsShader(v);
                StageShader.DeleteDiagnosticsShader(f);
                StageShader.DeleteDiagnosticsShader(g);
            }
        }
        catch (Exception ex)
        {
            log?.Warning($"[VGE][{ShaderName}] Failed to capture GL shader diagnostics: {ex}");
        }
    }

    protected virtual void RequestRecompile()
    {
        var api = capi;
        if (api is null)
        {
            return;
        }

        // Collapse multiple SetDefine calls into a single recompile.
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
                    CompileAndLink();
                }
                catch (Exception ex)
                {
                    log?.Error($"[VGE] Shader recompile failed for '{ShaderName}': {ex}");
                }
            },
            $"vge:recompile:{ShaderName}");
    }

    protected VgeShaderProgram()
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
}
