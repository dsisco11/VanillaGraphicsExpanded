using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;

using OpenTK.Graphics.OpenGL;
using Vintagestory.API.Common;

namespace VanillaGraphicsExpanded.Rendering;

/// <summary>
/// RAII wrapper around an OpenGL shader object (stage) built as a SPIR-V binary.
/// Deletion is deferred to <see cref="GpuResourceManager"/> when available.
/// All methods require a current GL context on the calling thread.
/// </summary>
internal sealed class GpuShaderModule : GpuResource, IDisposable
{
    private int shaderId;
    private readonly ShaderType shaderType;

    protected override nint ResourceId
    {
        get => shaderId;
        set => shaderId = (int)value;
    }

    protected override GpuResourceKind ResourceKind => GpuResourceKind.Shader;

    /// <summary>
    /// Gets the underlying OpenGL shader id.
    /// </summary>
    public int ShaderId => shaderId;

    /// <summary>Source-owned binding declarations carried into the program layout at link time.</summary>
    internal Contracts.GpuBindingContract? BindingContract { get; set; }

    /// <summary>
    /// Gets the OpenGL shader stage type.
    /// </summary>
    public ShaderType ShaderType => shaderType;

    /// <summary>
    /// Returns <c>true</c> when the shader has a non-zero id and has not been disposed.
    /// </summary>
    public new bool IsValid => shaderId != 0 && !IsDisposed;

    /// <summary>Adopts an owned shader handle loaded by the binary asset loader.</summary>
    internal GpuShaderModule(int shaderId, ShaderType shaderType)
    {
        this.shaderId = shaderId;
        this.shaderType = shaderType;
    }

    /// <summary>
    /// Returns true if the current GL context reports support for loading SPIR-V binaries via <c>GL_ARB_gl_spirv</c>.
    /// </summary>
    public static bool SupportsSpirv()
    {
        if (GlExtensions.Supports("GL_ARB_gl_spirv")) return true;
        if (!GlExtensions.TryGetContextKey(out _)) return false;
        GL.GetInteger(GetPName.MajorVersion, out int major);
        GL.GetInteger(GetPName.MinorVersion, out int minor);
        return major > 4 || (major == 4 && minor >= 6);
    }

    /// <summary>
    /// Sets the debug label for this shader (debug builds only).
    /// </summary>
    public override void SetDebugName(string? debugName)
    {
#if DEBUG
        if (shaderId != 0)
        {
            GlDebug.TryLabel(ObjectLabelIdentifier.Shader, shaderId, debugName);
        }
#endif
    }

    /// <summary>
    /// Creates a new shader module by compiling GLSL source via <c>glCompileShader</c>.
    /// This overload does not run VGE shader preprocessing; use <see cref="TryCompileGlslPreprocessed"/> to support <c>@import</c> and <c>#line</c> directives.
    /// </summary>
    /// <param name="shaderType">Shader stage type.</param>
    /// <param name="glslSource">GLSL source.</param>
    /// <param name="module">The created module on success; otherwise null.</param>
    /// <param name="infoLog">The shader compiler info log (may be empty).</param>
    /// <param name="debugName">Optional KHR_debug label (debug builds only).</param>
    /// <returns>True on successful compilation.</returns>
    public static bool TryCompileGlsl(
        ShaderType shaderType,
        string glslSource,
        out GpuShaderModule? module,
        out string infoLog,
        string? debugName = null)
    {
        module = null;
        infoLog = string.Empty;

        if (string.IsNullOrEmpty(glslSource))
        {
            return false;
        }

        int id = 0;
        try
        {
            id = GL.CreateShader(shaderType);
            if (id == 0)
            {
                return false;
            }

            GL.ShaderSource(id, glslSource);
            GL.CompileShader(id);

            GL.GetShader(id, ShaderParameter.CompileStatus, out int status);
            infoLog = GL.GetShaderInfoLog(id) ?? string.Empty;

            if (status == 0)
            {
                try { GL.DeleteShader(id); } catch { }
                return false;
            }

            var created = new GpuShaderModule(id, shaderType);
            created.SetDebugName(debugName);
            module = created;
            return true;
        }
        catch
        {
            try
            {
                if (id != 0)
                {
                    GL.DeleteShader(id);
                }
            }
            catch
            {
            }

            module = null;
            return false;
        }
    }

    /// <summary>
    /// Creates a new shader module by running VGE preprocessing (defines + <c>@import</c> + optional <c>#line</c> injection)
    /// and then compiling the resulting GLSL source via <c>glCompileShader</c>.
    /// </summary>
    /// <param name="api">Vintage Story API (used to load imported shader assets for <c>@import</c> and <c>#line</c> mapping).</param>
    /// <param name="shaderType">Shader stage type.</param>
    /// <param name="shaderName">Logical shader name (used for bookkeeping; can be the program name).</param>
    /// <param name="stageExtension">Stage extension (e.g. <c>vsh</c>, <c>fsh</c>).</param>
    /// <param name="glslSource">Raw GLSL source text (may contain <c>@import</c> directives).</param>
    /// <param name="module">The created module on success; otherwise null.</param>
    /// <param name="sourceCode">The preprocessed source bundle (includes emitted GLSL + optional source-id mapping).</param>
    /// <param name="infoLog">The shader compiler info log (may be empty).</param>
    /// <param name="defines">Optional preprocessor defines injected after <c>#version</c>.</param>
    /// <param name="debugName">Optional KHR_debug label (debug builds only).</param>
    /// <param name="log">Optional logger for preprocessing diagnostics.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>True on successful preprocessing + compilation.</returns>
    public static bool TryCompileGlslPreprocessed(
        ICoreAPI api,
        ShaderType shaderType,
        string shaderName,
        string stageExtension,
        string glslSource,
        out GpuShaderModule? module,
        out global::VanillaGraphicsExpanded.ShaderSourceCode? sourceCode,
        out string infoLog,
        IReadOnlyDictionary<string, string?>? defines = null,
        string? debugName = null,
        ILogger? log = null,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(api);
        ArgumentException.ThrowIfNullOrWhiteSpace(shaderName);
        ArgumentException.ThrowIfNullOrWhiteSpace(stageExtension);

        module = null;
        sourceCode = null;
        infoLog = string.Empty;

        if (string.IsNullOrEmpty(glslSource))
        {
            return false;
        }

        string sourceName = $"{shaderName}.{stageExtension}";

        try
        {
            sourceCode = global::VanillaGraphicsExpanded.ShaderSourceCode.FromSourceAssetBacked(
                api: api,
                shaderName: shaderName,
                stageExtension: stageExtension,
                sourceName: sourceName,
                rawSource: glslSource,
                defines: defines,
                log: log,
                ct: ct);
        }
        catch (Exception ex)
        {
            infoLog = $"[VGE] Shader preprocessing failed for '{shaderName}.{stageExtension}': {ex.Message}";
            return false;
        }

        // Compile the emitted source after preprocessing.
        if (!TryCompileGlsl(shaderType, sourceCode.EmittedSource, out module, out infoLog, debugName))
        {
            // VGE shader failures can generate very large sources; avoid dumping the full shader text into logs.
            // Instead, dump the uploaded source to GamePaths.Logs/VGE/<shader-name>.dump.txt.
            if (VgeShaderSourceDump.TryDumpSingleStage(shaderName, stageExtension, sourceCode.EmittedSource, out string? dumpPath, out string? dumpError))
            {
                infoLog = (infoLog.Length > 0 ? infoLog + "\n" : string.Empty) + $"[VGE] Shader source dumped to: {dumpPath}";
            }
            else if (!string.IsNullOrWhiteSpace(dumpError))
            {
                infoLog = (infoLog.Length > 0 ? infoLog + "\n" : string.Empty) + $"[VGE] Failed to dump shader source: {dumpError}";
            }

            // Preserve preprocessing diagnostics when compilation fails.
            if (sourceCode.ImportInlining.Diagnostics.Length > 0)
            {
                infoLog = infoLog + "\n[VGE] Preprocessor diagnostics:\n" + string.Join("\n", sourceCode.ImportInlining.Diagnostics);
            }

            module = null;
            return false;
        }

        if (module is null)
        {
            infoLog = "[VGE] Shader compilation succeeded but module was null (unexpected).";
            return false;
        }

        return true;
    }

    /// <summary>
    /// Creates a new shader module from a SPIR-V binary via <c>glShaderBinary</c> + <c>glSpecializeShader</c>.
    /// </summary>
    /// <remarks>
    /// Requires <c>GL_ARB_gl_spirv</c> (or GL 4.6 core). Use <see cref="SupportsSpirv"/> to preflight.
    /// With deferred completion, the returned handle is submitted but not yet validated; its owner must wait and check status.
    /// </remarks>
    public static unsafe bool TryLoadSpirv(
        ShaderType shaderType,
        ReadOnlySpan<byte> spirvBytes,
        string entryPoint,
        ReadOnlySpan<SpirvSpecializationConstant> specializationConstants,
        out GpuShaderModule? module,
        out string infoLog,
        string? debugName = null, bool deferCompletion = false)
    {
        module = null;
        infoLog = string.Empty;

        if (spirvBytes.Length < 20 || spirvBytes.Length % 4 != 0 || BinaryPrimitives.ReadUInt32LittleEndian(spirvBytes) != 0x07230203)
        {
            infoLog = "Invalid SPIR-V binary header.";
            return false;
        }
        if (string.IsNullOrWhiteSpace(entryPoint))
        {
            infoLog = "SPIR-V entry point is empty.";
            return false;
        }

        if (!SupportsSpirv())
        {
            infoLog = "GL_ARB_gl_spirv not supported by current context.";
            return false;
        }

#if DEBUG
        using var errors = new GlDebug.ErrorScope($"SPIR-V {debugName} ({shaderType}), entry {entryPoint}");
#endif
        int id = 0;
        try
        {
            id = GL.CreateShader(shaderType);
            if (id == 0)
            {
                infoLog = "glCreateShader returned 0.";
                return false;
            }

            int[] shaders = [id];

            long started = Stopwatch.GetTimestamp();
            fixed (byte* binaryPtr = spirvBytes)
            {
                GL.ShaderBinary(
                    1,
                    shaders,
                    ShaderBinaryFormat.ShaderBinaryFormatSpirV,
                    (IntPtr)binaryPtr,
                    spirvBytes.Length);
            }

#if DEBUG
            GlDebug.ThrowIfErrors($"{debugName}: glShaderBinary shader={id}, bytes={spirvBytes.Length}");
#endif
            LastBinaryLoadMilliseconds = Stopwatch.GetElapsedTime(started).TotalMilliseconds;
            int count = specializationConstants.Length;
            int[] indices = count == 0 ? Array.Empty<int>() : new int[count];
            int[] values = count == 0 ? Array.Empty<int>() : new int[count];

            for (int i = 0; i < count; i++)
            {
                indices[i] = specializationConstants[i].ConstantId;
                values[i] = specializationConstants[i].Value;
            }

            started = Stopwatch.GetTimestamp();
            GL.SpecializeShader(id, entryPoint, count, indices, values);
#if DEBUG
            GlDebug.ThrowIfErrors($"{debugName}: glSpecializeShader shader={id}, entry={entryPoint}, constants={count}");
#endif
            LastSpecializeMilliseconds = Stopwatch.GetElapsedTime(started).TotalMilliseconds;

            if (!deferCompletion)
            {
                GL.GetShader(id, ShaderParameter.CompileStatus, out int status);
                infoLog = GL.GetShaderInfoLog(id) ?? string.Empty;
                if (status == 0)
                {
                    try { GL.DeleteShader(id); } catch { }
                    return false;
                }
            }
            var created = new GpuShaderModule(id, shaderType);
            created.SetDebugName(debugName);
            module = created;
            return true;
        }
        catch (Exception ex)
        {
            infoLog = ex.Message;
            try
            {
                if (id != 0)
                {
                    GL.DeleteShader(id);
                }
            }
            catch
            {
            }

            module = null;
            return false;
        }
    }

    /// <summary>Last driver binary upload duration, excluding specialization.</summary>
    internal static double LastBinaryLoadMilliseconds { get; private set; }
    /// <summary>Last driver specialization duration, excluding linking and drawing.</summary>
    internal static double LastSpecializeMilliseconds { get; private set; }

    /// <summary>Typed specialization bits passed to OpenGL.</summary>
    public readonly record struct SpirvSpecializationConstant(int ConstantId, int Value);
}
