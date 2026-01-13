using System;
using System.Collections.Generic;
using System.Threading;

using VanillaGraphicsExpanded.PBR;
using VanillaGraphicsExpanded.Rendering;

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

        // Keep AssetDomain aligned with the import system default unless a derived program explicitly overrides it.
        if (string.IsNullOrWhiteSpace(AssetDomain))
        {
            AssetDomain = ShaderImportsSystem.DefaultDomain;
        }
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

        EnsureStageShadersAllocated(capi);

        IReadOnlyDictionary<string, string?> defineSnapshot;
        lock (defineLock)
        {
            defineSnapshot = new Dictionary<string, string?>(defines, StringComparer.Ordinal);
        }

        var vsh = ShaderSourceCode.Load(capi, ShaderName, "vsh", defineSnapshot, log);
        var fsh = ShaderSourceCode.Load(capi, ShaderName, "fsh", defineSnapshot, log);

        VertexShader.Code = vsh.EmittedSource;
        FragmentShader.Code = fsh.EmittedSource;

        // Geometry shader is optional.
        ShaderSourceCode? gshCode = null;
        if (TryLoadOptionalGeometryStage(capi, defineSnapshot, out var gsh))
        {
            gshCode = gsh;
            GeometryShader ??= (Shader)capi.Shader.NewShader(EnumShaderType.GeometryShader);
            GeometryShader.Code = gsh!.EmittedSource;
        }

        bool ok = Compile();
        if (ok)
        {
            GlDebug.TryLabel(OpenTK.Graphics.OpenGL.ObjectLabelIdentifier.Program, ProgramId, ShaderName);
            OnAfterCompile();
        }
        else
        {
            log?.Warning($"[VGE] Shader compile failed: {ShaderName}");

            // Best-effort diagnostics: recompile the current sources through OpenGL directly so we can
            // capture per-stage info logs even if the engine doesn't surface them.
            try
            {
                var diag = GlslCompileDiagnostics.TryCompile(
                    vertexSource: VertexShader.Code,
                    fragmentSource: FragmentShader.Code,
                    geometrySource: GeometryShader?.Code);

                if (diag.HasAnyLog)
                {
                    if (!string.IsNullOrWhiteSpace(diag.VertexLog))
                    {
                        log?.Error($"[VGE][{ShaderName}] Vertex shader info log:\n{diag.VertexLog}");
                    }
                    if (!string.IsNullOrWhiteSpace(diag.FragmentLog))
                    {
                        log?.Error($"[VGE][{ShaderName}] Fragment shader info log:\n{diag.FragmentLog}");
                    }
                    if (!string.IsNullOrWhiteSpace(diag.GeometryLog))
                    {
                        log?.Error($"[VGE][{ShaderName}] Geometry shader info log:\n{diag.GeometryLog}");
                    }
                    if (!string.IsNullOrWhiteSpace(diag.ProgramLog))
                    {
                        log?.Error($"[VGE][{ShaderName}] Program link info log:\n{diag.ProgramLog}");
                    }
                }

                // Emit some context that will matter for future remapping.
                if (vsh.ImportInlining.HadImports)
                {
                    log?.Notification($"[VGE][{ShaderName}] Vertex stage had imports; source-map available={vsh.ImportResult?.GetType().GetProperty("SourceMap")?.GetValue(vsh.ImportResult) is not null}");
                }
                if (fsh.ImportInlining.HadImports)
                {
                    log?.Notification($"[VGE][{ShaderName}] Fragment stage had imports; source-map available={fsh.ImportResult?.GetType().GetProperty("SourceMap")?.GetValue(fsh.ImportResult) is not null}");
                }
                if (gshCode is not null && gshCode.ImportInlining.HadImports)
                {
                    log?.Notification($"[VGE][{ShaderName}] Geometry stage had imports; source-map available={gshCode.ImportResult?.GetType().GetProperty("SourceMap")?.GetValue(gshCode.ImportResult) is not null}");
                }
            }
            catch (Exception ex)
            {
                log?.Warning($"[VGE][{ShaderName}] Failed to capture GL shader diagnostics: {ex}");
            }
        }

        return ok;
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

    private void EnsureStageShadersAllocated(ICoreClientAPI api)
    {
        VertexShader ??= (Shader)api.Shader.NewShader(EnumShaderType.VertexShader);
        FragmentShader ??= (Shader)api.Shader.NewShader(EnumShaderType.FragmentShader);
    }

    private bool TryLoadOptionalGeometryStage(
        ICoreClientAPI api,
        IReadOnlyDictionary<string, string?> defineSnapshot,
        out ShaderSourceCode? gsh)
    {
        gsh = null;

        string domain = ShaderImportsSystem.DefaultDomain;
        string stageSourceName = $"{ShaderName}.gsh";
        string assetPath = $"shaders/{stageSourceName}";

        IAsset? asset = api.Assets.TryGet(AssetLocation.Create(assetPath, domain), loadAsset: true);
        if (asset is null)
        {
            return false;
        }

        gsh = ShaderSourceCode.Load(api, ShaderName, "gsh", defineSnapshot, log);
        return true;
    }
}
