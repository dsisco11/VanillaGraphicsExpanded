using System;
using System.Collections.Generic;
using System.Diagnostics;

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

    private readonly Dictionary<string, int> uniformLocationCache = new(StringComparer.Ordinal);
    private int uniformLocationCacheProgramId;

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

    private GpuComputePipeline(int programId, GpuProgramLayout programLayout)
    {
        this.programId = programId;
        this.programLayout = programLayout;
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
        bool disposeShaderAfterLink = true)
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

            var layout = GpuProgramLayout.TryBuild(programId);
            pipeline = new GpuComputePipeline(programId, layout);
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
        System.Threading.CancellationToken ct = default)
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

        if (!TryCreate(module, out pipeline, out string linkLog, debugName, disposeShaderAfterLink: true))
        {
            infoLog = (infoLog.Length > 0 ? infoLog + "\n" : string.Empty) + linkLog;
            pipeline = null;
            return false;
        }

        return true;
    }

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

    #region Uniforms (Cached)

    /// <summary>
    /// Returns the cached uniform location for <paramref name="uniformName"/>.
    /// When <paramref name="uniformName"/> refers to an array, this method also tries <c>name[0]</c>.
    /// </summary>
    public int GetUniformLocationOrArray0(string uniformName)
    {
        if (programId == 0 || string.IsNullOrWhiteSpace(uniformName))
        {
            return -1;
        }

        if (uniformLocationCacheProgramId != programId)
        {
            uniformLocationCache.Clear();
            uniformLocationCacheProgramId = programId;
        }

        if (uniformLocationCache.TryGetValue(uniformName, out int cached))
        {
            return cached;
        }

        int loc = GL.GetUniformLocation(programId, uniformName);
        if (loc < 0)
        {
            loc = GL.GetUniformLocation(programId, $"{uniformName}[0]");
        }

        uniformLocationCache[uniformName] = loc;
        return loc;
    }

    /// <summary>
    /// Returns <c>true</c> if the currently linked program exposes the uniform.
    /// </summary>
    public bool HasUniform(string uniformName)
    {
        return GetUniformLocationOrArray0(uniformName) >= 0;
    }

    /// <summary>
    /// Sets an <c>int</c> uniform. Requires this program to be bound (use <see cref="Use"/> / <see cref="UseScope"/>).
    /// Returns false if the uniform is missing.
    /// </summary>
    public bool TrySetUniform1(string uniformName, int value)
    {
        if (!IsValid)
        {
            return false;
        }

        int loc = GetUniformLocationOrArray0(uniformName);
        if (loc < 0)
        {
            return false;
        }

        try
        {
            GL.Uniform1(loc, value);
            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Sets a <c>uint</c> uniform. Requires this program to be bound (use <see cref="Use"/> / <see cref="UseScope"/>).
    /// Returns false if the uniform is missing.
    /// </summary>
    public bool TrySetUniform1(string uniformName, uint value)
    {
        if (!IsValid)
        {
            return false;
        }

        int loc = GetUniformLocationOrArray0(uniformName);
        if (loc < 0)
        {
            return false;
        }

        try
        {
            GL.Uniform1(loc, value);
            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Sets a <c>float</c> uniform. Requires this program to be bound (use <see cref="Use"/> / <see cref="UseScope"/>).
    /// Returns false if the uniform is missing.
    /// </summary>
    public bool TrySetUniform1(string uniformName, float value)
    {
        if (!IsValid)
        {
            return false;
        }

        int loc = GetUniformLocationOrArray0(uniformName);
        if (loc < 0)
        {
            return false;
        }

        try
        {
            GL.Uniform1(loc, value);
            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Sets a <c>bool</c> uniform (0/1). Requires this program to be bound (use <see cref="Use"/> / <see cref="UseScope"/>).
    /// Returns false if the uniform is missing.
    /// </summary>
    public bool TrySetUniform1(string uniformName, bool value)
    {
        return TrySetUniform1(uniformName, value ? 1 : 0);
    }

    /// <summary>
    /// Sets an <c>ivec2</c> uniform. Requires this program to be bound (use <see cref="Use"/> / <see cref="UseScope"/>).
    /// Returns false if the uniform is missing.
    /// </summary>
    public bool TrySetUniform2(string uniformName, int x, int y)
    {
        if (!IsValid)
        {
            return false;
        }

        int loc = GetUniformLocationOrArray0(uniformName);
        if (loc < 0)
        {
            return false;
        }

        try
        {
            GL.Uniform2(loc, x, y);
            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Sets a <c>uvec2</c> uniform. Requires this program to be bound (use <see cref="Use"/> / <see cref="UseScope"/>).
    /// Returns false if the uniform is missing.
    /// </summary>
    public bool TrySetUniform2(string uniformName, uint x, uint y)
    {
        if (!IsValid)
        {
            return false;
        }

        int loc = GetUniformLocationOrArray0(uniformName);
        if (loc < 0)
        {
            return false;
        }

        try
        {
            GL.Uniform2(loc, x, y);
            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Sets a <c>vec2</c> uniform. Requires this program to be bound (use <see cref="Use"/> / <see cref="UseScope"/>).
    /// Returns false if the uniform is missing.
    /// </summary>
    public bool TrySetUniform2(string uniformName, float x, float y)
    {
        if (!IsValid)
        {
            return false;
        }

        int loc = GetUniformLocationOrArray0(uniformName);
        if (loc < 0)
        {
            return false;
        }

        try
        {
            GL.Uniform2(loc, x, y);
            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Sets an <c>ivec3</c> uniform. Requires this program to be bound (use <see cref="Use"/> / <see cref="UseScope"/>).
    /// Returns false if the uniform is missing.
    /// </summary>
    public bool TrySetUniform3(string uniformName, int x, int y, int z)
    {
        if (!IsValid)
        {
            return false;
        }

        int loc = GetUniformLocationOrArray0(uniformName);
        if (loc < 0)
        {
            return false;
        }

        try
        {
            GL.Uniform3(loc, x, y, z);
            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Sets a <c>uvec3</c> uniform. Requires this program to be bound (use <see cref="Use"/> / <see cref="UseScope"/>).
    /// Returns false if the uniform is missing.
    /// </summary>
    public bool TrySetUniform3(string uniformName, uint x, uint y, uint z)
    {
        if (!IsValid)
        {
            return false;
        }

        int loc = GetUniformLocationOrArray0(uniformName);
        if (loc < 0)
        {
            return false;
        }

        try
        {
            GL.Uniform3(loc, x, y, z);
            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Sets a <c>vec3</c> uniform. Requires this program to be bound (use <see cref="Use"/> / <see cref="UseScope"/>).
    /// Returns false if the uniform is missing.
    /// </summary>
    public bool TrySetUniform3(string uniformName, float x, float y, float z)
    {
        if (!IsValid)
        {
            return false;
        }

        int loc = GetUniformLocationOrArray0(uniformName);
        if (loc < 0)
        {
            return false;
        }

        try
        {
            GL.Uniform3(loc, x, y, z);
            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Sets an <c>ivec4</c> uniform. Requires this program to be bound (use <see cref="Use"/> / <see cref="UseScope"/>).
    /// Returns false if the uniform is missing.
    /// </summary>
    public bool TrySetUniform4(string uniformName, int x, int y, int z, int w)
    {
        if (!IsValid)
        {
            return false;
        }

        int loc = GetUniformLocationOrArray0(uniformName);
        if (loc < 0)
        {
            return false;
        }

        try
        {
            GL.Uniform4(loc, x, y, z, w);
            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Sets a <c>uvec4</c> uniform. Requires this program to be bound (use <see cref="Use"/> / <see cref="UseScope"/>).
    /// Returns false if the uniform is missing.
    /// </summary>
    public bool TrySetUniform4(string uniformName, uint x, uint y, uint z, uint w)
    {
        if (!IsValid)
        {
            return false;
        }

        int loc = GetUniformLocationOrArray0(uniformName);
        if (loc < 0)
        {
            return false;
        }

        try
        {
            GL.Uniform4(loc, x, y, z, w);
            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Sets a <c>vec4</c> uniform. Requires this program to be bound (use <see cref="Use"/> / <see cref="UseScope"/>).
    /// Returns false if the uniform is missing.
    /// </summary>
    public bool TrySetUniform4(string uniformName, float x, float y, float z, float w)
    {
        if (!IsValid)
        {
            return false;
        }

        int loc = GetUniformLocationOrArray0(uniformName);
        if (loc < 0)
        {
            return false;
        }

        try
        {
            GL.Uniform4(loc, x, y, z, w);
            return true;
        }
        catch
        {
            return false;
        }
    }

    #endregion

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
        uniformLocationCache.Clear();
        uniformLocationCacheProgramId = 0;
    }
}
