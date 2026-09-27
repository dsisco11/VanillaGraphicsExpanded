using TinyTokenizer.Ast;
using VanillaGraphicsExpanded.PBR;

namespace VanillaGraphicsExpanded.Tests;

/// <summary>Exercises material capture against the installed engine shader layout.</summary>
public sealed class PbrTerrainColorPatchesTests
{
    #region Installed terrain boundaries
    /// <summary>The complete production patch chain keeps capture and restoration in the same executable scope.</summary>
    [Theory]
    [InlineData("chunkopaque.fsh")]
    [InlineData("chunktopsoil.fsh")]
    public void ProductionPatchChainRetainsMaterialScope(string name)
    {
        var tree = SyntaxTree.Parse(ReadShader(name), GlslSchema.Instance);
        Assert.True(VanillaShaderPatches.TryApplyPatches(null, tree, name));
        string actual = tree.ToText();
        Assert.Equal(ScopeAt(actual, "vec3 vge_materialColor ="), ScopeAt(actual, "outColor.rgb = vge_materialColor;"));
    }

    /// <summary>Capture runs after color mapping and before forward effects, preserving the original body exactly once.</summary>
    [Theory]
    [InlineData("chunkopaque.fsh", "texColor.rgb")]
    [InlineData("chunktopsoil.fsh", "outColor.rgb")]
    public void FragmentCapturePreservesBodyAndCapturesBeforeEffects(string name, string color)
    {
        string source = ReadShader(name);
        var tree = SyntaxTree.Parse(source, GlslSchema.Instance);
        string before = tree.ToText();
        Assert.Equal(source, before);
        PbrTerrainColorPatches.ApplyFragment(tree, name);
        string after = tree.ToText();
        string capture = $"vec3 vge_materialColor = VgeSrgbToLinear({color});\n    ";
        const string restore = "\n#if NORMALVIEW == 0\n    outColor.rgb = vge_materialColor;\n#endif\n";
        Assert.Contains(capture, after);
        Assert.True(after.IndexOf(capture, StringComparison.Ordinal) < after.IndexOf("float murkiness", StringComparison.Ordinal));
        Assert.Equal(ScopeAt(after, "vec3 vge_materialColor ="), ScopeAt(after, "outColor.rgb = vge_materialColor;"));
        Assert.Equal(before, after.Replace(capture, "", StringComparison.Ordinal).Replace(restore, "", StringComparison.Ordinal));
    }

    /// <summary>Vertex neutralization changes only RGB and leaves engine alpha and side outputs intact.</summary>
    [Theory]
    [InlineData("chunkopaque.vsh")]
    [InlineData("chunktopsoil.vsh")]
    public void VertexCapturePreservesOriginalBody(string name)
    {
        var tree = SyntaxTree.Parse(ReadShader(name), GlslSchema.Instance);
        string before = tree.ToText();
        PbrTerrainColorPatches.ApplyVertex(tree, name);
        string insertion = "\n    // Deferred lighting consumes unlit material RGB; retain the engine fade alpha.\n    rgba.rgb = vec3(1.0);\n";
        Assert.Contains(insertion, tree.ToText());
        Assert.Equal(before, tree.ToText().Replace(insertion, "", StringComparison.Ordinal));
    }

    /// <summary>Unsupported engine layouts fail instead of capturing a silently wrong color.</summary>
    [Fact]
    public void MissingBoundaryIsRejected()
    {
        var tree = SyntaxTree.Parse("void main() { outColor = vec4(1.0); }", GlslSchema.Instance);
        Assert.Throws<InvalidOperationException>(() => PbrTerrainColorPatches.ApplyFragment(tree, "chunkopaque.fsh"));
    }

    /// <summary>Restoration remains in the original scope without requiring a final glow output.</summary>
    [Fact]
    public void TrailingConditionalWithoutGlowRetainsMaterialScope()
    {
        var tree = SyntaxTree.Parse("void main() { vec4 texColor=vec4(1); float murkiness=0;\n#if NORMALVIEW > 0\noutColor=texColor;\n#endif\n}", GlslSchema.Instance);
        PbrTerrainColorPatches.ApplyFragment(tree, "chunkopaque.fsh");
        string actual = tree.ToText();
        Assert.Equal(ScopeAt(actual, "vec3 vge_materialColor ="), ScopeAt(actual, "outColor.rgb = vge_materialColor;"));
        Assert.True(actual.LastIndexOf("outColor.rgb = vge_materialColor;", StringComparison.Ordinal) > actual.IndexOf("#endif", StringComparison.Ordinal));
    }

    /// <summary>Transparent and liquid paths remain outside the deferred material capture change.</summary>
    [Theory]
    [InlineData("chunktransparent.fsh")]
    [InlineData("chunkliquid.fsh")]
    public void UnsupportedMaterialPathsRemainUnchanged(string name)
    {
        var tree = SyntaxTree.Parse(ReadShader(name), GlslSchema.Instance);
        string before = tree.ToText();
        PbrTerrainColorPatches.ApplyFragment(tree, name);
        Assert.Equal(before, tree.ToText());
    }
    #endregion

    #region Engine assets
    /// <summary>Tracks concrete lexical brace identities so capture and use must occupy the same nested scope.</summary>
    private static int[] ScopeAt(string source, string marker)
    {
        // Ignore comments so braces in documentation cannot masquerade as executable scopes.
        string code = System.Text.RegularExpressions.Regex.Replace(source, @"/\*[\s\S]*?\*/|//[^\r\n]*", "");
        int markerIndex = code.IndexOf(marker, StringComparison.Ordinal);
        Assert.True(markerIndex >= 0);
        var scope = new Stack<int>();
        int nextScope = 0;
        for (int i = 0; i < markerIndex; i++)
        {
            if (code[i] == '{') scope.Push(++nextScope);
            else if (code[i] == '}') scope.Pop();
        }
        return scope.Reverse().ToArray();
    }

    /// <summary>Reads the same local engine installation required by project references.</summary>
    private static string ReadShader(string name)
    {
        string root = Environment.GetEnvironmentVariable("VINTAGE_STORY") ?? throw new InvalidOperationException("VINTAGE_STORY must identify the test engine installation.");
        return File.ReadAllText(Path.Combine(root, "assets", "game", "shaders", name));
    }
    #endregion
}
