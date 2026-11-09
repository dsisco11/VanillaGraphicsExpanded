using TinyTokenizer.Ast;
using VanillaGraphicsExpanded.Tests.Support.Rendering.Shaders;
using Xunit;

namespace VanillaGraphicsExpanded.Tests.Unit.Rendering.Shaders;

/// <summary>Provides diagnostic token output for GLSL tokenizer behavior.</summary>
public sealed class ShaderTokenizerDiagnosticsTests
{
    #region Public API
    /// <summary>Captures diagnostic output from the parser test.</summary>
    public ShaderTokenizerDiagnosticsTests(ITestOutputHelper output)
    {
        this.output = output;
    }

    /// <summary>Writes the parsed token tree to test output for diagnostic inspection.</summary>
    [Fact]
    public void Diagnostic_DumpTokens()
    {
        // Arrange
        const string source = ShaderPatchingTestSources.SampleShader;
        // Act
        var tree = SyntaxTree.Parse(source, GlslSchema.Instance);
        var renderedTree = tree.ToString("S", null);
        // Assert by emitting the parsed tree for diagnostic inspection; this test defines no semantic token oracle.
        output.WriteLine(renderedTree);

    }
    #endregion

    #region Private
    private readonly ITestOutputHelper output;
    #endregion
}
