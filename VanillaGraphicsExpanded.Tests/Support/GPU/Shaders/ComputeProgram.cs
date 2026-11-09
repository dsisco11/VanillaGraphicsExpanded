using VanillaGraphicsExpanded.Rendering.ShaderCompilation;
using VanillaGraphicsExpanded.Rendering.Contracts;
using System;
using System.IO;

using VanillaGraphicsExpanded.Rendering;

using Xunit;

namespace VanillaGraphicsExpanded.Tests.Support.GPU.Shaders;

/// <summary>Loads built SPIR-V compute assets and owns their test program lifetime.</summary>
public sealed class ComputeProgram : IDisposable
{
    private ComputeShaderAsset? _pipeline;
    private bool _disposed;

    #region Public API

    /// <summary>Gets the native identity while retaining ownership through the compute asset.</summary>
    public int ProgramId => _pipeline?.ProgramId ?? 0;

    /// <summary>Loads the required build output, failing instead of falling back to runtime GLSL compilation.</summary>
    public static ComputeProgram Create(
        string programIdentity,
        string? debugName = null,
        GpuProgramLayout? layout = null,
        Action<string>? layoutWarn = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(programIdentity);
        var settings = new ShaderSettings(GpuShaderContracts.Registry.FindProgram(programIdentity));
        var selected = new ShaderLoadPlan(settings).Stages.Single();

        string spvPath = Path.Combine(AppContext.BaseDirectory, "assets", "shaders", selected.BinaryPath);

        Assert.True(GpuSupport.Graphics.SupportsArbGlSpirv, "GPU compute tests require SPIR-V support; GLSL fallback is not permitted.");

        bool ok = ComputeShaderAsset.TryLoadFromSpirv(
            spirvBinaryPath: spvPath,
            settings: settings,
            pipeline: out var pipeline,
            infoLog: out string infoLog,
            debugName: debugName, layout: layout, warn: layoutWarn, fileSystem: ShaderAssetFileSystem.Built);

        Assert.True(ok, $"SPIR-V compute program creation failed. InfoLog:\n{infoLog}");
        Assert.NotNull(pipeline);
        Assert.True(pipeline!.IsValid);
        Assert.True(pipeline.ProgramId != 0);



        return new ComputeProgram(pipeline);
    }

    /// <summary>Releases the program through its retained production asset owner.</summary>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        _pipeline?.Dispose();
        _pipeline = null;
    }

    #endregion

    #region Private

    /// <summary>Retains the loaded pipeline as the sole owner of the native compute program.</summary>
    private ComputeProgram(ComputeShaderAsset pipeline)
    {
        _pipeline = pipeline ?? throw new ArgumentNullException(nameof(pipeline));
    }
    #endregion
}
