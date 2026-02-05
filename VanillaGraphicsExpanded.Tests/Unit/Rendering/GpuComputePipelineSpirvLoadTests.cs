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
            pipeline: out var pipeline,
            infoLog: out string infoLog);

        Assert.False(ok);
        Assert.Null(pipeline);
        Assert.Contains("not found", infoLog, StringComparison.OrdinalIgnoreCase);
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
                pipeline: out var pipeline,
                infoLog: out string infoLog);

            Assert.False(ok);
            Assert.Null(pipeline);
            Assert.Contains("empty", infoLog, StringComparison.OrdinalIgnoreCase);
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
