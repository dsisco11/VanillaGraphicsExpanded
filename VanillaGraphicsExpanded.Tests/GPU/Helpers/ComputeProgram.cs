using System;
using System.Collections.Concurrent;
using System.IO;
using System.Text.RegularExpressions;

using OpenTK.Graphics.OpenGL;

using VanillaGraphicsExpanded.Rendering;

using Xunit;

namespace VanillaGraphicsExpanded.Tests.GPU.Helpers;

public sealed class ComputeProgram : IDisposable
{
    private static readonly ConcurrentDictionary<int, string> ProgramIdToShaderFile = new();
    private static readonly ConcurrentDictionary<string, ConcurrentDictionary<string, int>> ShaderFileToUniformLocationCache = new(StringComparer.OrdinalIgnoreCase);

    private int _programId;
    private GpuComputePipeline? _pipeline;
    private bool _disposed;

    public string ShaderFile { get; }

    public int ProgramId => _programId;

    private ComputeProgram(int programId, string shaderFile)
    {
        _programId = programId;
        ShaderFile = shaderFile;
        ProgramIdToShaderFile[programId] = shaderFile;
    }

    private ComputeProgram(GpuComputePipeline pipeline, string shaderFile)
    {
        _pipeline = pipeline ?? throw new ArgumentNullException(nameof(pipeline));
        _programId = pipeline.ProgramId;
        ShaderFile = shaderFile;
        ProgramIdToShaderFile[_programId] = shaderFile;
    }

    public static ComputeProgram Create(
        ShaderTestHelper helper,
        string computeShaderFile,
        string? debugName = null,
        bool preferSpirv = true,
        GpuProgramLayout? layout = null,
        Action<string>? layoutWarn = null)
    {
        ArgumentNullException.ThrowIfNull(helper);
        ArgumentException.ThrowIfNullOrWhiteSpace(computeShaderFile);

        string spvPath = Path.Combine(AppContext.BaseDirectory, "assets", "shaders", computeShaderFile + ".spv");

        if (preferSpirv && GpuShaderModule.SupportsSpirv())
        {
            Assert.SkipWhen(!File.Exists(spvPath), $"SPIR-V test asset missing: {spvPath}");

            bool ok = GpuComputePipeline.TryLoadFromSpirv(
                spirvBinaryPath: spvPath,
                pipeline: out var pipeline,
                infoLog: out string infoLog,
                debugName: debugName);

            Assert.True(ok, $"SPIR-V compute program creation failed. InfoLog:\n{infoLog}");
            Assert.NotNull(pipeline);
            Assert.True(pipeline!.IsValid);
            Assert.True(pipeline.ProgramId != 0);

            layout?.ApplyContract(pipeline.ProgramId, warn: layoutWarn);

            return new ComputeProgram(pipeline, computeShaderFile);
        }

        var cs = helper.CompileShader(computeShaderFile, ShaderType.ComputeShader);
        Assert.True(cs.IsSuccess, cs.ErrorMessage);

        int program = GL.CreateProgram();
        GL.AttachShader(program, cs.ShaderId);
        GL.LinkProgram(program);

        GL.GetProgram(program, GetProgramParameterName.LinkStatus, out int okLink);
        string linkLog = GL.GetProgramInfoLog(program) ?? string.Empty;
        Assert.True(okLink != 0, $"Compute program link failed:\n{linkLog}");

        layout?.ApplyContract(program, warn: layoutWarn);

        return new ComputeProgram(program, computeShaderFile);
    }

    public static bool TryGetExplicitUniformLocation(int programId, string uniformName, out int location)
    {
        location = -1;

        if (string.IsNullOrWhiteSpace(uniformName))
        {
            return false;
        }

        if (!ProgramIdToShaderFile.TryGetValue(programId, out string? shaderFile) || string.IsNullOrWhiteSpace(shaderFile))
        {
            return false;
        }

        var perShaderCache = ShaderFileToUniformLocationCache.GetOrAdd(shaderFile, _ => new ConcurrentDictionary<string, int>(StringComparer.OrdinalIgnoreCase));

        string baseUniformName = uniformName;
        int arrayIndex = -1;
        if (TryParseUniformArrayElement(uniformName, out string parsedBase, out int parsedIndex))
        {
            baseUniformName = parsedBase;
            arrayIndex = parsedIndex;
        }

        if (perShaderCache.TryGetValue(uniformName, out location))
        {
            return true;
        }

        if (arrayIndex >= 0 && perShaderCache.TryGetValue(baseUniformName, out int cachedBaseLocation))
        {
            location = cachedBaseLocation + arrayIndex;
            perShaderCache[uniformName] = location;
            return true;
        }

        if (!TryReadShaderSource(shaderFile, out string? source) || string.IsNullOrWhiteSpace(source))
        {
            return false;
        }

        // Match: layout(location = N) uniform <type> <name>
        var regex = new Regex(
            $@"layout\s*\(\s*location\s*=\s*(?<loc>\d+)\s*\)\s*uniform\s+[^;]*\b{Regex.Escape(baseUniformName)}\b",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        var match = regex.Match(source);
        if (!match.Success)
        {
            return false;
        }

        if (!int.TryParse(match.Groups["loc"].Value, out location))
        {
            return false;
        }

        perShaderCache[baseUniformName] = location;
        if (arrayIndex >= 0)
        {
            location += arrayIndex;
        }

        perShaderCache[uniformName] = location;
        return true;
    }

    private static bool TryParseUniformArrayElement(string uniformName, out string baseName, out int index)
    {
        baseName = string.Empty;
        index = -1;

        int open = uniformName.IndexOf('[');
        int close = uniformName.IndexOf(']', open + 1);

        if (open <= 0 || close <= open + 1)
        {
            return false;
        }

        string candidateBase = uniformName[..open].Trim();
        if (string.IsNullOrWhiteSpace(candidateBase))
        {
            return false;
        }

        string indexText = uniformName[(open + 1)..close].Trim();
        if (!int.TryParse(indexText, out int parsed) || parsed < 0)
        {
            return false;
        }

        baseName = candidateBase;
        index = parsed;
        return true;
    }

    private static bool TryReadShaderSource(string computeShaderFile, out string? source)
    {
        source = null;

        if (TryGetRepoShaderPath(out string repoShaderPath))
        {
            string path = Path.Combine(repoShaderPath, computeShaderFile);
            if (File.Exists(path))
            {
                source = File.ReadAllText(path);
                return true;
            }
        }

        string fallbackPath = Path.Combine(AppContext.BaseDirectory, "assets", "shaders", computeShaderFile);
        if (File.Exists(fallbackPath))
        {
            source = File.ReadAllText(fallbackPath);
            return true;
        }

        return false;
    }

    private static bool TryGetRepoShaderPath(out string shaderPath)
    {
        shaderPath = string.Empty;

        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        for (int i = 0; i < 8 && dir != null; i++)
        {
            string sln = Path.Combine(dir.FullName, "VanillaGraphicsExpanded.sln");
            if (File.Exists(sln))
            {
                string candidateShader = Path.Combine(
                    dir.FullName,
                    "VanillaGraphicsExpanded",
                    "assets",
                    "vanillagraphicsexpanded",
                    "shaders");

                if (Directory.Exists(candidateShader))
                {
                    shaderPath = candidateShader;
                    return true;
                }
            }

            dir = dir.Parent;
        }

        return false;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        if (_pipeline is not null)
        {
            _pipeline.Dispose();
            _pipeline = null;

            ProgramIdToShaderFile.TryRemove(_programId, out _);
            _programId = 0;
            return;
        }

        if (_programId != 0)
        {
            GL.DeleteProgram(_programId);

            ProgramIdToShaderFile.TryRemove(_programId, out _);
            _programId = 0;
        }
    }
}
