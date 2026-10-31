using VanillaGraphicsExpanded.Tests.TestSupport;
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
    private readonly CountingDiskCacheFileSystem storage = new();
    [Fact]
    public void TryLoadFromSpirv_EmptyPath_ReturnsFalse()
    {
        bool ok = ComputeShaderAsset.TryLoadFromSpirv(
            spirvBinaryPath: "",
            settings: new ShaderSettings(GpuShaderContracts.Registry.FindProgram("lumonscene_feedback_mark_pages")),
            pipeline: out var pipeline,
            infoLog: out string infoLog, fileSystem: storage);

        Assert.False(ok);
        Assert.Null(pipeline);
        Assert.Contains("path", infoLog, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void TryLoadFromSpirv_MissingFile_ReturnsFalse()
    {
        string path = Path.Combine(Path.GetTempPath(), $"vge-missing-{Guid.NewGuid():N}.spv");

        bool ok = ComputeShaderAsset.TryLoadFromSpirv(
            spirvBinaryPath: path,
            settings: new ShaderSettings(GpuShaderContracts.Registry.FindProgram("lumonscene_feedback_mark_pages")),
            pipeline: out var pipeline,
            infoLog: out string infoLog, fileSystem: storage);

        Assert.False(ok);
        Assert.Null(pipeline);
        Assert.Contains(path, infoLog, StringComparison.Ordinal);
    }

    [Fact]
    public void TryLoadFromSpirv_EmptyFile_ReturnsFalse()
    {
        string path = Path.Combine(Path.GetTempPath(), $"vge-empty-{Guid.NewGuid():N}.spv");

        try
        {
            storage.WriteAllBytes(path, Array.Empty<byte>());

            bool ok = ComputeShaderAsset.TryLoadFromSpirv(
                spirvBinaryPath: path,
                settings: new ShaderSettings(GpuShaderContracts.Registry.FindProgram("lumonscene_feedback_mark_pages")),
            pipeline: out var pipeline,
                infoLog: out string infoLog, fileSystem: storage);

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
}
