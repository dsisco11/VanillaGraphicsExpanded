using System;
using System.Buffers.Binary;
using System.IO;
using VanillaGraphicsExpanded.Rendering.RHI;

namespace VanillaGraphicsExpanded.Tests.Unit.Rendering;

/// <summary>Checks entry-point admission using packaged shader bytes without requiring a graphics context.</summary>
public sealed class RHIShaderStageDescriptionTests
{
    #region Public API
    /// <summary>Accepts the declared vertex entry point but rejects a different stage or missing name before native ingestion.</summary>
    [Fact]
    public void SelectedEntryPointMustMatchNameAndStage()
    {
        byte[] bytes = VertexBinary();
        var stage = new RHIShaderStageDescription(ShaderStage.Vertex, bytes);
        Assert.Equal("main", stage.EntryPoint);
        var mismatch = Assert.Throws<ArgumentException>(() => new RHIShaderStageDescription(ShaderStage.Fragment, bytes));
        Assert.Contains("specialization entry point", mismatch.Message);
        Assert.Throws<ArgumentException>(() => new RHIShaderStageDescription(ShaderStage.Vertex, bytes, "missing"));
    }
    /// <summary>Invalid instruction lengths cannot loop indefinitely or traverse beyond the supplied binary.</summary>
    [Theory]
    [InlineData(0u)]
    [InlineData(uint.MaxValue)]
    public void MalformedInstructionExtentRejectsBeforeNativeIngestion(uint instruction)
    {
        byte[] bytes = VertexBinary();
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(5 * sizeof(uint)), instruction);
        var error = Assert.Throws<ArgumentException>(() => new RHIShaderStageDescription(ShaderStage.Vertex, bytes));
        Assert.Contains("instruction extent", error.Message);
    }
    #endregion

    #region Private
    /// <summary>Reads the canonical precompiled vertex fixture used by executable lifecycle tests.</summary>
    private static byte[] VertexBinary() => File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory,
        "assets", "shaders", "tests", "render_infrastructure.vsh.spv"));
    #endregion
}
