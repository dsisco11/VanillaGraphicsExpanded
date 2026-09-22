using System;
using System.Collections.Concurrent;
using System.IO;
using System.Text.RegularExpressions;

using OpenTK.Graphics.OpenGL;

using VanillaGraphicsExpanded.Rendering;

using Xunit;

namespace VanillaGraphicsExpanded.Tests.GPU.Helpers;

/// <summary>Loads built SPIR-V compute assets and owns their test program lifetime.</summary>
public sealed class ComputeProgram : IDisposable
{
    private static readonly ConcurrentDictionary<int, string> ProgramIdToShaderFile = new();

    private int _programId;
    private GpuComputePipeline? _pipeline;
    private bool _disposed;

    public string ShaderFile { get; }

    public int ProgramId => _programId;

    /// <summary>Retains the loaded pipeline and its uniform lookup identity.</summary>
    private ComputeProgram(GpuComputePipeline pipeline, string shaderFile)
    {
        _pipeline = pipeline ?? throw new ArgumentNullException(nameof(pipeline));
        _programId = pipeline.ProgramId;
        ShaderFile = shaderFile;
        TestShaderInterfaces.TrackProgram(_programId, pipeline.ProgramLayout.BinaryInterface!);
        ProgramIdToShaderFile[_programId] = shaderFile;
    }

    /// <summary>Loads the required build output, failing instead of falling back to runtime GLSL compilation.</summary>
    public static ComputeProgram Create(
        ShaderTestHelper helper,
        string computeShaderFile,
        string? debugName = null,
        GpuProgramLayout? layout = null,
        Action<string>? layoutWarn = null)
    {
        ArgumentNullException.ThrowIfNull(helper);
        ArgumentException.ThrowIfNullOrWhiteSpace(computeShaderFile);

        string spvPath = Path.Combine(AppContext.BaseDirectory, "assets", "shaders", computeShaderFile + ".spv");

        Assert.True(GpuShaderModule.SupportsSpirv(), "GPU compute tests require SPIR-V support; GLSL fallback is not permitted.");
        Assert.True(File.Exists(spvPath), $"SPIR-V test asset missing: {spvPath}. Build the test project before running tests.");

        bool ok = GpuComputePipeline.TryLoadFromSpirv(
            spirvBinaryPath: spvPath,
            pipeline: out var pipeline,
            infoLog: out string infoLog,
            debugName: debugName, layout: layout, warn: layoutWarn);

        Assert.True(ok, $"SPIR-V compute program creation failed. InfoLog:\n{infoLog}");
        Assert.NotNull(pipeline);
        Assert.True(pipeline!.IsValid);
        Assert.True(pipeline.ProgramId != 0);



        return new ComputeProgram(pipeline, computeShaderFile);
    }

    /// <summary>Uses the built binary contract rather than source-text layout guesses.</summary>
    public static bool TryGetExplicitUniformLocation(int programId, string uniformName, out int location)
    {
        location = global::VanillaGraphicsExpanded.Tests.GPU.Helpers.TestShaderInterfaces.GetUniformLocation(programId, uniformName);
        return location >= 0;
    }
    /// <summary>Releases the program and its numeric resource metadata.</summary>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        if (_pipeline is not null)
        {
            TestShaderInterfaces.ForgetProgram(_programId);
            _pipeline.Dispose();
            _pipeline = null;

            ProgramIdToShaderFile.TryRemove(_programId, out _);
            _programId = 0;
            return;
        }

        if (_programId != 0)
        {
            global::VanillaGraphicsExpanded.Tests.GPU.Helpers.TestShaderInterfaces.DeleteProgram(_programId);

            ProgramIdToShaderFile.TryRemove(_programId, out _);
            _programId = 0;
        }
    }
}
