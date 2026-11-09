using VanillaGraphicsExpanded.Tests.Support.Cache;
using VanillaGraphicsExpanded.Rendering.ShaderCompilation;
using VanillaGraphicsExpanded.Rendering.Contracts;
using System;
using System.IO;
using VanillaGraphicsExpanded.Rendering;
using Xunit;

namespace VanillaGraphicsExpanded.Tests.Unit.Rendering;

/// <summary>Checks explicit file-loader failure contracts against an isolated mocked filesystem.</summary>
public sealed class GpuComputePipelineSpirvLoadTests
{
    #region Public API

    /// <summary>An empty path is rejected and returns diagnostic information without a pipeline.</summary>
    [Fact]
    public void TryLoadFromSpirv_EmptyPath_ReturnsFalse()
    {
        // Arrange
        // Act
        bool ok = ComputeShaderAsset.TryLoadFromSpirv(
            spirvBinaryPath: "",
            settings: new ShaderSettings(GpuShaderContracts.Registry.FindProgram("lumonscene_feedback_mark_pages")),
            pipeline: out var pipeline,
            infoLog: out string infoLog, fileSystem: storage);

        // Assert
        Assert.False(ok);
        Assert.Null(pipeline);
        Assert.Contains("path", infoLog, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>A nonexistent file is rejected with its path included in the diagnostic.</summary>
    [Fact]
    public void TryLoadFromSpirv_MissingFile_ReturnsFalse()
    {
        // Arrange
        string path = Path.Combine(Path.GetTempPath(), $"vge-missing-{Guid.NewGuid():N}.spv");

        // Act
        bool ok = ComputeShaderAsset.TryLoadFromSpirv(
            spirvBinaryPath: path,
            settings: new ShaderSettings(GpuShaderContracts.Registry.FindProgram("lumonscene_feedback_mark_pages")),
            pipeline: out var pipeline,
            infoLog: out string infoLog, fileSystem: storage);

        // Assert
        Assert.False(ok);
        Assert.Null(pipeline);
        Assert.Contains(path, infoLog, StringComparison.Ordinal);
    }

    /// <summary>An empty SPIR-V file is rejected with the program asset identified.</summary>
    [Fact]
    public void TryLoadFromSpirv_EmptyFile_ReturnsFalse()
    {
        // Arrange
        string path = Path.Combine(Path.GetTempPath(), $"vge-empty-{Guid.NewGuid():N}.spv");

        try
        {
            storage.WriteAllBytes(path, Array.Empty<byte>());

            // Act
            bool ok = ComputeShaderAsset.TryLoadFromSpirv(
                spirvBinaryPath: path,
                settings: new ShaderSettings(GpuShaderContracts.Registry.FindProgram("lumonscene_feedback_mark_pages")),
            pipeline: out var pipeline,
                infoLog: out string infoLog, fileSystem: storage);

            // Assert
            Assert.False(ok);
            Assert.Null(pipeline);
            Assert.Contains("SPIR-V", infoLog, StringComparison.Ordinal);
            Assert.Contains("lumonscene_feedback_mark_pages.csh", infoLog, StringComparison.Ordinal);
        }
        finally
        {
            if (storage.FileExists(path))
            {
                storage.DeleteFile(path);
            }
        }
    }

    #endregion

    #region Private

    private readonly CountingDiskCacheFileSystem storage = new();

    #endregion
}
