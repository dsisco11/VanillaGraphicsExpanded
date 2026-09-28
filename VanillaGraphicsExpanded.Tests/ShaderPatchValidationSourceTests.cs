using VanillaGraphicsExpanded.HarmonyPatches;

namespace VanillaGraphicsExpanded.Tests;

/// <summary>Protects the separation of engine prefixes from published shader bodies.</summary>
public sealed class ShaderPatchValidationSourceTests
{
    #region Validation source
    /// <summary>Keeps the version first and extension declarations before shader statements.</summary>
    [Theory]
    [InlineData("\n")]
    [InlineData("\r\n")]
    public void PrefixFollowsVersionWithoutChangingCandidate(string newline)
    {
        string source = $"#version 330 core{newline}#extension GL_ARB_explicit_attrib_location : enable{newline}void main() {{ }}";
        const string prefix = "#define MAXANIMATEDELEMENTS 46";
        string validation = ShaderIncludesHook.BuildValidationSource(source, prefix);
        string[] lines = validation.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        Assert.Equal(["#version 330 core", prefix, "#extension GL_ARB_explicit_attrib_location : enable", "void main() { }"], lines);
        Assert.DoesNotContain(prefix, source);
    }

    /// <summary>A commented version is not mistaken for the actual GLSL directive.</summary>
    [Fact]
    public void PrefixIgnoresCommentedVersion()
    {
        const string source = """
            // #version 120
            #version 330 core
            void main() { }
            """;
        const string prefix = "#define MAXANIMATEDELEMENTS 46";
        string validation = ShaderIncludesHook.BuildValidationSource(source, prefix);
        string[] lines = validation.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        Assert.Equal(["// #version 120", "#version 330 core", prefix, "void main() { }"], lines);
    }

    /// <summary>No engine definitions require no allocation or source modification.</summary>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void EmptyPrefixPreservesSource(string? prefix)
    {
        const string source = "#version 330 core\nvoid main() { }";
        Assert.Same(source, ShaderIncludesHook.BuildValidationSource(source, prefix));
    }
    #endregion
}
