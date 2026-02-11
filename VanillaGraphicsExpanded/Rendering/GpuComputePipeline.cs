using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading;

using OpenTK.Graphics.OpenGL;

using Vintagestory.API.Common;

namespace VanillaGraphicsExpanded.Rendering;

/// <summary>
/// RAII wrapper around an OpenGL compute program (a linked program containing a compute shader stage).
/// Deletion is deferred to <see cref="GpuResourceManager"/> when available.
/// All methods require a current GL context on the calling thread.
/// </summary>
internal sealed class GpuComputePipeline : GpuResource, IDisposable
{
    private int programId;
    private GpuProgramLayout programLayout = GpuProgramLayout.Empty;

    private Action<string>? warn;

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
    /// <returns>True if linking succeeded.</returns>
    public static bool TryCreate(
        GpuShaderModule computeShader,
        out GpuComputePipeline? pipeline,
        out string infoLog,
        string? debugName = null,
        bool disposeShaderAfterLink = true,
        GpuProgramLayout? layout = null,
        Action<string>? warn = null)
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
        try
        {
            programId = GL.CreateProgram();
            if (programId == 0)
            {
                infoLog = "glCreateProgram returned 0.";
                return false;
            }

#if DEBUG
            GlDebug.TryLabel(ObjectLabelIdentifier.Program, programId, debugName);
#endif

            GL.AttachShader(programId, computeShader.ShaderId);
            GL.LinkProgram(programId);

            GL.GetProgram(programId, GetProgramParameterName.LinkStatus, out int linkStatus);
            infoLog = GL.GetProgramInfoLog(programId) ?? string.Empty;

            GL.DetachShader(programId, computeShader.ShaderId);

            if (disposeShaderAfterLink)
            {
                computeShader.Dispose();
            }

            if (linkStatus == 0)
            {
                try { GL.DeleteProgram(programId); } catch { }
                return false;
            }

            var layoutToUse = layout ?? new GpuProgramLayout();
            layoutToUse.ApplyContract(programId, warn);
#if DEBUG
            layoutToUse.ValidateContract(programId, warn);
#endif

            pipeline = new GpuComputePipeline(programId, layoutToUse, warn);
            pipeline.SetDebugName(debugName);
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
    }

    /// <summary>
    /// Compiles a compute shader from preprocessed GLSL and links it into a compute pipeline.
    /// </summary>
    /// <param name="api">Vintage Story API (used for preprocessing imports and <c>#line</c> mapping).</param>
    /// <param name="shaderName">Logical shader name (used for preprocessing bookkeeping).</param>
    /// <param name="glslSource">Raw GLSL source text (may contain <c>@import</c> directives).</param>
    /// <param name="pipeline">The created pipeline instance on success; otherwise null.</param>
    /// <param name="sourceCode">Preprocessed source bundle (includes emitted GLSL and optional source mapping).</param>
    /// <param name="infoLog">Compiler/linker info log (may be empty).</param>
    /// <param name="stageExtension">Stage extension used for preprocessing bookkeeping (defaults to <c>csh</c>).</param>
    /// <param name="defines">Optional preprocessor defines injected after <c>#version</c>.</param>
    /// <param name="debugName">Optional KHR_debug label (debug builds only).</param>
    /// <param name="log">Optional logger for preprocessing diagnostics.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>True on successful compilation + link.</returns>
    public static bool TryCompileAndCreateGlslPreprocessed(
        ICoreAPI api,
        string shaderName,
        string glslSource,
        out GpuComputePipeline? pipeline,
        out global::VanillaGraphicsExpanded.ShaderSourceCode? sourceCode,
        out string infoLog,
        string stageExtension = "csh",
        System.Collections.Generic.IReadOnlyDictionary<string, string?>? defines = null,
        string? debugName = null,
        ILogger? log = null,
        System.Threading.CancellationToken ct = default,
        GpuProgramLayout? layout = null)
    {
        ArgumentNullException.ThrowIfNull(api);
        ArgumentException.ThrowIfNullOrWhiteSpace(shaderName);

        pipeline = null;
        sourceCode = null;
        infoLog = string.Empty;

        if (!GpuShaderModule.TryCompileGlslPreprocessed(
            api: api,
            shaderType: ShaderType.ComputeShader,
            shaderName: shaderName,
            stageExtension: stageExtension,
            glslSource: glslSource,
            module: out var module,
            sourceCode: out sourceCode,
            infoLog: out infoLog,
            defines: defines,
            debugName: debugName,
            log: log,
            ct: ct))
        {
            module?.Dispose();
            pipeline = null;
            return false;
        }

        if (module is null)
        {
            infoLog = "[VGE] Compute shader compilation succeeded but module was null (unexpected).";
            pipeline = null;
            return false;
        }

        if (!TryCreate(
            computeShader: module,
            pipeline: out pipeline,
            infoLog: out string linkLog,
            debugName: debugName,
            disposeShaderAfterLink: true,
            layout: layout,
            warn: log is null ? null : msg => log.Warning($"[VGE][Compute:{shaderName}] {msg}")))
        {
            infoLog = (infoLog.Length > 0 ? infoLog + "\n" : string.Empty) + linkLog;
            pipeline = null;
            return false;
        }

        return true;
    }

    private static int spirvUnsupportedLogged;
    private static int spirvUsedCount;
    private static int spirvMissingCount;
    private static int spirvUnsupportedCount;

    public static bool TryLoadFromSpirv(
        string spirvBinaryPath,
        out GpuComputePipeline? pipeline,
        out string infoLog,
        string? debugName = null,
        GpuProgramLayout? layout = null,
        Action<string>? warn = null)
    {
        pipeline = null;
        infoLog = string.Empty;

        if (string.IsNullOrWhiteSpace(spirvBinaryPath))
        {
            infoLog = "[VGE] SPIR-V path was null/empty.";
            return false;
        }

        if (!File.Exists(spirvBinaryPath))
        {
            infoLog = $"[VGE] SPIR-V file not found: {spirvBinaryPath}";
            return false;
        }

        byte[] bytes;
        try
        {
            bytes = File.ReadAllBytes(spirvBinaryPath);
        }
        catch (Exception ex)
        {
            infoLog = $"[VGE] Failed to read SPIR-V file '{spirvBinaryPath}': {ex.Message}";
            return false;
        }

        if (bytes.Length == 0)
        {
            infoLog = $"[VGE] SPIR-V file was empty: {spirvBinaryPath}";
            return false;
        }

        bool ok = TryCreateFromSpirvBytes(bytes, out pipeline, out string spirvLog, debugName, layout, warn);
        infoLog = spirvLog;
        return ok;
    }

    private static bool TryCreateFromSpirvBytes(
        ReadOnlySpan<byte> spirvBytes,
        out GpuComputePipeline? pipeline,
        out string infoLog,
        string? debugName = null,
        GpuProgramLayout? layout = null,
        Action<string>? warn = null)
    {
        pipeline = null;
        infoLog = string.Empty;

        if (spirvBytes.Length == 0)
        {
            infoLog = "[VGE] SPIR-V bytes were empty.";
            return false;
        }

        if (!GpuShaderModule.SupportsSpirv())
        {
            infoLog = "[VGE] GL_ARB_gl_spirv not supported by current context.";
            return false;
        }

        if (!GpuShaderModule.TryLoadSpirv(
            shaderType: ShaderType.ComputeShader,
            spirvBytes: spirvBytes,
            entryPoint: "main",
            specializationConstants: ReadOnlySpan<GpuShaderModule.SpirvSpecializationConstant>.Empty,
            module: out var module,
            infoLog: out string spirvLog,
            debugName: debugName))
        {
            infoLog = spirvLog;
            return false;
        }

        if (module is null)
        {
            infoLog = (spirvLog.Length > 0 ? spirvLog + "\n" : string.Empty) + "[VGE] SPIR-V load succeeded but module was null (unexpected).";
            return false;
        }

        if (!TryCreate(
            computeShader: module,
            pipeline: out pipeline,
            infoLog: out string linkLog,
            debugName: debugName,
            disposeShaderAfterLink: true,
            layout: layout,
            warn: warn))
        {
            module.Dispose();
            pipeline = null;
            infoLog = (spirvLog.Length > 0 ? spirvLog + "\n" : string.Empty) + linkLog;
            return false;
        }

        Interlocked.Increment(ref spirvUsedCount);
        infoLog = (spirvLog.Length > 0 ? spirvLog + "\n" : string.Empty) + linkLog;
        return true;
    }

    /// <summary>
    /// Creates a compute pipeline by loading a packaged SPIR-V binary (<c>.csh.spv</c>) when <paramref name="preferSpirv" /> is true,
    /// present, and supported; otherwise falls back to loading and compiling the GLSL source (<c>.csh</c>) through the existing
    /// preprocessing pipeline.
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
        bool preferSpirv = false,
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

        string stageName = $"{shaderName}.{stageExtension}";
        string spvStageName = stageName + ".spv";

        // 1) Optionally prefer SPIR-V if present and supported.
        if (preferSpirv)
        {
            var locSpv = AssetLocation.Create($"shaders/{spvStageName}", VanillaGraphicsExpanded.PBR.ShaderImportsSystem.DefaultDomain);
            IAsset? spvAsset = api.Assets.TryGet(locSpv, loadAsset: true);
            if (spvAsset is not null)
            {
                if (GpuShaderModule.SupportsSpirv())
                {
                    byte[] bytes = spvAsset.Data ?? Array.Empty<byte>();
                    if (bytes.Length > 0)
                    {
                        Action<string>? warn = (log ?? api.Logger) is null
                            ? null
                            : msg => (log ?? api.Logger).Warning($"[VGE][Compute:{shaderName}] {msg}");

                        if (TryCreateFromSpirvBytes(bytes, out pipeline, out string spirvInfo, debugName, layout, warn))
                        {
                            infoLog = spirvInfo;
                            return true;
                        }

                        // Fall back to GLSL when SPIR-V fails to load/link.
                        infoLog = (spirvInfo.Length > 0 ? spirvInfo + "\n" : string.Empty) + "[VGE] SPIR-V load/link failed; falling back to GLSL.";
                    }
                    else
                    {
                        Interlocked.Increment(ref spirvMissingCount);
                    }
                }
                else
                {
                    Interlocked.Increment(ref spirvUnsupportedCount);

                    // Log once per session when SPIR-V exists but isn't supported.
                    if (Interlocked.Exchange(ref spirvUnsupportedLogged, 1) == 0)
                    {
                        (log ?? api.Logger)?.Warning("[VGE] SPIR-V binaries found but GL_ARB_gl_spirv is not supported; falling back to GLSL.");
                    }
                }
            }
            else
            {
                Interlocked.Increment(ref spirvMissingCount);
            }
        }

        // 2) GLSL fallback: load the source asset and use existing preprocessing + compilation.
        var loc = AssetLocation.Create($"shaders/{stageName}", VanillaGraphicsExpanded.PBR.ShaderImportsSystem.DefaultDomain);
        IAsset? asset = api.Assets.TryGet(loc, loadAsset: true);
        if (asset is null)
        {
            infoLog = $"[VGE] Missing compute shader asset: {loc}";
            pipeline = null;
            return false;
        }

        string src = asset.ToText();
        if (!TryCompileAndCreateGlslPreprocessed(
            api: api,
            shaderName: shaderName,
            glslSource: src,
            pipeline: out pipeline,
            sourceCode: out sourceCode,
            infoLog: out string glslLog,
            stageExtension: stageExtension,
            defines: defines,
            debugName: debugName,
            log: log,
            ct: ct,
            layout: layout))
        {
            infoLog = (infoLog.Length > 0 ? infoLog + "\n" : string.Empty) + glslLog;
            pipeline = null;
            return false;
        }

        infoLog = (infoLog.Length > 0 ? infoLog + "\n" : string.Empty) + glslLog;
        return pipeline is not null;
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
        if (!IsValid)
        {
            Debug.WriteLine("[GpuComputePipeline] Attempted to use disposed or invalid program");
            return;
        }

        GlStateCache.Current.UseProgram(programId);
    }

    /// <summary>
    /// Attempts to use this compute pipeline. Returns <c>false</c> if invalid.
    /// </summary>
    public bool TryUse()
    {
        if (!IsValid)
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
        var scope = GlStateCache.Current.UseProgramScope(programId);
        return new ProgramScope(scope);
    }

    /// <summary>
    /// Dispatches compute work via <c>glDispatchCompute</c>, binding this program for the duration of the call.
    /// </summary>
    public void Dispatch(int numGroupsX, int numGroupsY = 1, int numGroupsZ = 1)
    {
        if (!IsValid)
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

        if (!IsValid || !indirectBuffer.IsValid)
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
