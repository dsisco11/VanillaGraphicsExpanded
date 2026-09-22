using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
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
#if DEBUG
        using var errors = new GlDebug.ErrorScope($"{debugName}: compute linking and bindings");
#endif
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
#if DEBUG
            GlDebug.ThrowIfErrors($"{debugName}: compute create/attach/link program={programId}");
#endif

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
            if (computeShader.BindingContract != null)
                layoutToUse.BinaryInterface = new Spirv.GpuProgramInterface(programId, [computeShader.BindingContract]);
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

    private static int spirvUsedCount;

    /// <summary>Loads a standalone compute binary using its source-owned binding contract.</summary>
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

        try
        {
            if (new FileInfo(spirvBinaryPath).Length == 0) throw new InvalidOperationException("SPIR-V file was empty: " + spirvBinaryPath);
            if (!GpuShaderModule.TryLoadSpirv(ShaderType.ComputeShader, File.ReadAllBytes(spirvBinaryPath),
                "main", [], out var module, out infoLog, debugName) || module == null) return false;
            using (module)
            {
                module.BindingContract = Contracts.GpuShaderContracts.Create(Path.GetFileNameWithoutExtension(Path.GetFileNameWithoutExtension(spirvBinaryPath)));
                return TryCreate(module, out pipeline, out infoLog, debugName, layout: layout, warn: warn);
            }
        }
        catch (Exception ex) { infoLog = ex.Message; return false; }
    }

    /// <summary>Loads a contract-selected binary and links its name-independent resource contract.</summary>
    private static bool TryCreateFromBinaryContract(string source, IReadOnlyDictionary<string, string?>? defines,
        Spirv.ShaderAssetReader read, out GpuComputePipeline? pipeline, out string infoLog,
        string? debugName, GpuProgramLayout? layout, Action<string>? warn)
    {
        pipeline = null;
        try
        {
            var loaded = Spirv.SpirvStageLoader.Load(source, ShaderType.ComputeShader, defines, read);
            using var module = new GpuShaderModule(loaded.Shader, ShaderType.ComputeShader) { BindingContract = loaded.Contract };
            bool success = TryCreate(module, out pipeline, out infoLog, debugName, layout: layout, warn: warn);
            if (success) Interlocked.Increment(ref spirvUsedCount);
            return success;
        }
        catch (Exception ex) { infoLog = ex.Message; return false; }
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
        string stageName = $"{shaderName}.{stageExtension}";
        ReadOnlySpan<byte> Read(string path) => api.Assets.TryGet(AssetLocation.Create("shaders/" + path,
            PBR.ShaderImportsSystem.DefaultDomain), loadAsset: true)?.Data
            ?? throw new InvalidOperationException("Missing built compute shader asset: " + path);
        return TryCreateFromBinaryContract(stageName, defines, Read, out pipeline, out infoLog,
            debugName, layout, message => (log ?? api.Logger)?.Warning(message));
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
