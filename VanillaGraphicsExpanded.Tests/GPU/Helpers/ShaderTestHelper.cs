using System;
using System.Collections.Generic;
using System.IO;
using Silk.NET.OpenGL;
using TinyTokenizer.Ast;

namespace VanillaGraphicsExpanded.Tests.GPU.Helpers;

/// <summary>
/// Helper class for compiling and linking GLSL shaders in tests.
/// Uses the production AST-based @import processing system.
/// </summary>
public sealed class ShaderTestHelper : IDisposable
{
    private readonly GL _gl;
    private readonly string _shaderBasePath;
    private readonly string _includeBasePath;
    private readonly List<uint> _allocatedShaders = [];
    private readonly List<uint> _allocatedPrograms = [];
    
    // Lazy-loaded imports cache
    private Dictionary<string, string>? _importsCache;

    /// <summary>
    /// Creates a new ShaderTestHelper with explicit paths.
    /// </summary>
    /// <param name="gl">The Silk.NET GL instance.</param>
    /// <param name="shaderBasePath">Base path for shader files (.vsh, .fsh).</param>
    /// <param name="includeBasePath">Base path for include files (shaderincludes/).</param>
    public ShaderTestHelper(GL gl, string shaderBasePath, string includeBasePath)
    {
        ArgumentNullException.ThrowIfNull(gl);
        ArgumentException.ThrowIfNullOrWhiteSpace(shaderBasePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(includeBasePath);

        _gl = gl;
        _shaderBasePath = shaderBasePath;
        _includeBasePath = includeBasePath;

        if (!Directory.Exists(_shaderBasePath))
            throw new DirectoryNotFoundException($"Shader base path not found: {_shaderBasePath}");

        if (!Directory.Exists(_includeBasePath))
            throw new DirectoryNotFoundException($"Include base path not found: {_includeBasePath}");
    }

    /// <summary>
    /// Gets or builds the imports cache from include files.
    /// Lazy-loaded on first shader compilation.
    /// </summary>
    private Dictionary<string, string> ImportsCache => _importsCache ??= BuildImportsCache();

    /// <summary>
    /// Builds the imports cache by reading all include files from the include base path.
    /// </summary>
    private Dictionary<string, string> BuildImportsCache()
    {
        var cache = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        
        // Scan for all shader include files (.fsh, .vsh, .glsl)
        var patterns = new[] { "*.fsh", "*.vsh", "*.glsl" };
        foreach (var pattern in patterns)
        {
            foreach (var filePath in Directory.EnumerateFiles(_includeBasePath, pattern, SearchOption.TopDirectoryOnly))
            {
                var fileName = Path.GetFileName(filePath);
                if (!cache.ContainsKey(fileName))
                {
                    cache[fileName] = File.ReadAllText(filePath);
                }
            }
        }
        
        return cache;
    }

    /// <summary>
    /// Compiles a shader from a file.
    /// </summary>
    /// <param name="filename">Shader filename (e.g., "lumon_gather.fsh").</param>
    /// <param name="type">Shader type (Vertex or Fragment).</param>
    /// <returns>Result containing shader ID on success, or error message on failure.</returns>
    public ShaderCompileResult CompileShader(string filename, ShaderType type)
    {
        var filePath = Path.Combine(_shaderBasePath, filename);
        if (!File.Exists(filePath))
        {
            return ShaderCompileResult.Failure($"Shader file not found: {filePath}");
        }

        try
        {
            // Read shader source
            var source = File.ReadAllText(filePath);
            
            // Process @import directives using the production AST-based system
            var processedSource = ProcessImports(source);

            // Strip non-ASCII characters using the production SIMD-optimized method
            processedSource = SourceCodeImportsProcessor.StripNonAscii(processedSource);

            // Add #version if not present (some VS shaders omit it)
            if (!processedSource.TrimStart().StartsWith("#version"))
            {
                processedSource = "#version 330 core\n" + processedSource;
            }

            // Create and compile shader
            uint shaderId = _gl.CreateShader(type);
            _allocatedShaders.Add(shaderId);

            _gl.ShaderSource(shaderId, processedSource);
            _gl.CompileShader(shaderId);

            // Check compilation status
            _gl.GetShader(shaderId, ShaderParameterName.CompileStatus, out int status);
            if (status == 0)
            {
                var infoLog = _gl.GetShaderInfoLog(shaderId);
                return ShaderCompileResult.Failure($"Compilation failed for {filename}:\n{infoLog}");
            }

            return ShaderCompileResult.Success(shaderId);
        }
        catch (Exception ex)
        {
            return ShaderCompileResult.Failure($"Exception compiling {filename}: {ex.Message}");
        }
    }

    /// <summary>
    /// Links vertex and fragment shaders into a program.
    /// </summary>
    /// <param name="vertexShaderId">Compiled vertex shader ID.</param>
    /// <param name="fragmentShaderId">Compiled fragment shader ID.</param>
    /// <returns>Result containing program ID on success, or error message on failure.</returns>
    public ProgramLinkResult LinkProgram(uint vertexShaderId, uint fragmentShaderId)
    {
        try
        {
            uint programId = _gl.CreateProgram();
            _allocatedPrograms.Add(programId);

            _gl.AttachShader(programId, vertexShaderId);
            _gl.AttachShader(programId, fragmentShaderId);
            _gl.LinkProgram(programId);

            // Check link status
            _gl.GetProgram(programId, ProgramPropertyARB.LinkStatus, out int status);
            if (status == 0)
            {
                var infoLog = _gl.GetProgramInfoLog(programId);
                return ProgramLinkResult.Failure($"Link failed:\n{infoLog}");
            }

            return ProgramLinkResult.Success(programId);
        }
        catch (Exception ex)
        {
            return ProgramLinkResult.Failure($"Exception linking program: {ex.Message}");
        }
    }

    /// <summary>
    /// Compiles and links a shader program from vertex and fragment shader files.
    /// </summary>
    /// <param name="vertexFilename">Vertex shader filename.</param>
    /// <param name="fragmentFilename">Fragment shader filename.</param>
    /// <returns>Result containing program ID on success, or error message on failure.</returns>
    public ProgramLinkResult CompileAndLink(string vertexFilename, string fragmentFilename)
    {
        var vertexResult = CompileShader(vertexFilename, ShaderType.VertexShader);
        if (!vertexResult.IsSuccess)
        {
            return ProgramLinkResult.Failure($"Vertex shader error: {vertexResult.ErrorMessage}");
        }

        var fragmentResult = CompileShader(fragmentFilename, ShaderType.FragmentShader);
        if (!fragmentResult.IsSuccess)
        {
            return ProgramLinkResult.Failure($"Fragment shader error: {fragmentResult.ErrorMessage}");
        }

        return LinkProgram(vertexResult.ShaderId, fragmentResult.ShaderId);
    }

    /// <summary>
    /// Gets the location of a uniform variable in a shader program.
    /// </summary>
    /// <param name="programId">The shader program ID.</param>
    /// <param name="uniformName">The name of the uniform.</param>
    /// <returns>The uniform location, or -1 if not found.</returns>
    public int GetUniformLocation(uint programId, string uniformName)
    {
        return _gl.GetUniformLocation(programId, uniformName);
    }

    /// <summary>
    /// Processes @import directives in shader source using the production AST-based system.
    /// </summary>
    /// <param name="source">The shader source code.</param>
    /// <returns>Processed source with imports inlined.</returns>
    private string ProcessImports(string source)
    {
        var tree = SyntaxTree.Parse(source, GlslSchema.Instance);
        SourceCodeImportsProcessor.ProcessImports(tree, ImportsCache, logger: null);
        return tree.ToText();
    }

    /// <summary>
    /// Disposes all allocated shader and program resources.
    /// </summary>
    public void Dispose()
    {
        foreach (var programId in _allocatedPrograms)
        {
            if (programId != 0)
            {
                _gl.DeleteProgram(programId);
            }
        }
        _allocatedPrograms.Clear();

        foreach (var shaderId in _allocatedShaders)
        {
            if (shaderId != 0)
            {
                _gl.DeleteShader(shaderId);
            }
        }
        _allocatedShaders.Clear();
    }
}

/// <summary>
/// Result of a shader compilation operation.
/// </summary>
public readonly struct ShaderCompileResult
{
    public bool IsSuccess { get; }
    public uint ShaderId { get; }
    public string? ErrorMessage { get; }

    private ShaderCompileResult(bool success, uint shaderId, string? error)
    {
        IsSuccess = success;
        ShaderId = shaderId;
        ErrorMessage = error;
    }

    public static ShaderCompileResult Success(uint shaderId) => new(true, shaderId, null);
    public static ShaderCompileResult Failure(string error) => new(false, 0, error);
}

/// <summary>
/// Result of a program link operation.
/// </summary>
public readonly struct ProgramLinkResult
{
    public bool IsSuccess { get; }
    public uint ProgramId { get; }
    public string? ErrorMessage { get; }

    private ProgramLinkResult(bool success, uint programId, string? error)
    {
        IsSuccess = success;
        ProgramId = programId;
        ErrorMessage = error;
    }

    public static ProgramLinkResult Success(uint programId) => new(true, programId, null);
    public static ProgramLinkResult Failure(string error) => new(false, 0, error);
}
