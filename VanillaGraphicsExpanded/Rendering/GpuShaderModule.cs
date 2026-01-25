using System;
using System.Collections.Generic;
using System.Threading;

using OpenTK.Graphics.OpenGL;

using Vintagestory.API.Common;

namespace VanillaGraphicsExpanded.Rendering;

/// <summary>
/// RAII wrapper around an OpenGL shader object (stage), with optional SPIR-V loading support.
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

    /// <summary>
    /// Gets the OpenGL shader stage type.
    /// </summary>
    public ShaderType ShaderType => shaderType;

    /// <summary>
    /// Returns <c>true</c> when the shader has a non-zero id and has not been disposed.
    /// </summary>
    public new bool IsValid => shaderId != 0 && !IsDisposed;

    private GpuShaderModule(int shaderId, ShaderType shaderType)
    {
        this.shaderId = shaderId;
        this.shaderType = shaderType;
    }

    /// <summary>
    /// Returns true if the current GL context reports support for loading SPIR-V binaries via <c>GL_ARB_gl_spirv</c>.
    /// </summary>
    public static bool SupportsSpirv()
    {
        return GlExtensions.Supports("GL_ARB_gl_spirv");
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
    /// </remarks>
    public static unsafe bool TryLoadSpirv(
        ShaderType shaderType,
        ReadOnlySpan<byte> spirvBytes,
        string entryPoint,
        ReadOnlySpan<SpirvSpecializationConstant> specializationConstants,
        out GpuShaderModule? module,
        out string infoLog,
        string? debugName = null)
    {
        module = null;
        infoLog = string.Empty;

        if (spirvBytes.Length == 0 || string.IsNullOrWhiteSpace(entryPoint))
        {
            return false;
        }

        if (!SupportsSpirv())
        {
            infoLog = "GL_ARB_gl_spirv not supported by current context.";
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

            int[] shaders = [id];

            fixed (byte* binaryPtr = spirvBytes)
            {
                GL.ShaderBinary(
                    1,
                    shaders,
                    ShaderBinaryFormat.ShaderBinaryFormatSpirV,
                    (IntPtr)binaryPtr,
                    spirvBytes.Length);
            }

            int count = specializationConstants.Length;
            int[] indices = count == 0 ? Array.Empty<int>() : new int[count];
            int[] values = count == 0 ? Array.Empty<int>() : new int[count];

            for (int i = 0; i < count; i++)
            {
                indices[i] = specializationConstants[i].ConstantId;
                values[i] = specializationConstants[i].Value;
            }

            GL.SpecializeShader(id, entryPoint, count, indices, values);

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
    /// SPIR-V specialization constant id/value pair.
    /// </summary>
    public readonly record struct SpirvSpecializationConstant(int ConstantId, int Value);
}
