using VanillaGraphicsExpanded.Rendering.Contracts;
using System;
using System.IO;
using VanillaGraphicsExpanded.Rendering;
using Xunit;

namespace VanillaGraphicsExpanded.Tests.Unit.Rendering;

public sealed class GpuComputePipelineSpirvLoadTests
{
    [Fact]
    public void TryLoadFromSpirv_EmptyPath_ReturnsFalse()
    {
        bool ok = GpuComputePipeline.TryLoadFromSpirv(
            spirvBinaryPath: "",
            settings: new ShaderSettings(GpuShaderContracts.Registry.FindProgram("lumonscene_feedback_mark_pages")),
            pipeline: out var pipeline,
            infoLog: out string infoLog);

        Assert.False(ok);
        Assert.Null(pipeline);
        Assert.Contains("path", infoLog, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void TryLoadFromSpirv_MissingFile_ReturnsFalse()
    {
        string path = Path.Combine(Path.GetTempPath(), $"vge-missing-{Guid.NewGuid():N}.spv");

        bool ok = GpuComputePipeline.TryLoadFromSpirv(
            spirvBinaryPath: path,
            settings: new ShaderSettings(GpuShaderContracts.Registry.FindProgram("lumonscene_feedback_mark_pages")),
            pipeline: out var pipeline,
            infoLog: out string infoLog);

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
            File.WriteAllBytes(path, Array.Empty<byte>());

            bool ok = GpuComputePipeline.TryLoadFromSpirv(
                spirvBinaryPath: path,
                settings: new ShaderSettings(GpuShaderContracts.Registry.FindProgram("lumonscene_feedback_mark_pages")),
            pipeline: out var pipeline,
                infoLog: out string infoLog);

            Assert.False(ok);
            Assert.Null(pipeline);
            Assert.Contains("SPIR-V", infoLog, StringComparison.Ordinal);
            Assert.Contains("lumonscene_feedback_mark_pages.csh", infoLog, StringComparison.Ordinal);
        }
        finally
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
    }
}
