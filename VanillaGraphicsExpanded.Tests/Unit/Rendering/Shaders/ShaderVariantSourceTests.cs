using ShaderBuildTool.Spirv;
using VanillaGraphicsExpanded.Rendering.Contracts;

namespace VanillaGraphicsExpanded.Tests.Unit.Rendering.Shaders;

/// <summary>Checks source-emission rejection of absent or ambiguous shader language versions.</summary>
public sealed class ShaderVariantSourceTests
{
    #region Public API
    /// <summary>Absent or duplicate language versions are rejected during source emission.</summary>
    [Theory]
    [InlineData("void main() {}")]
    [InlineData("#version 450 core\n#version 450 core\nvoid main() {}")]
    public void InvalidVersionDirectivesFail(string source)
    {
        // Arrange a valid compute selection paired with the authored source text.
        var stage = new ShaderStageContract("empty.csh", "empty.csh", ShaderStageKind.Compute, new());
        var selected = new ShaderVariantResolver([new GpuShaderContract("empty", [stage], 1)]).Binaries[0];
        // Act and assert absent or duplicate version declarations are rejected.
        Assert.Throws<InvalidOperationException>(() => new ShaderVariantSource(".", "fixture").Emit(source, selected));
    }
    #endregion
}
