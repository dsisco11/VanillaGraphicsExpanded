using TinyTokenizer.Ast;
using VanillaGraphicsExpanded.PBR;

namespace VanillaGraphicsExpanded.Tests.Unit.Terrain;

/// <summary>Verifies isolated terrain material-capture rewrite rules on controlled source strings.</summary>
public sealed class PbrTerrainColorPatchRulesTests
{
    #region Public API
    /// <summary>A missing source boundary is rejected instead of silently applying a misplaced capture.</summary>
    [Fact]
    public void MissingBoundaryIsRejected()
    {
        // Arrange: provide a minimal source with no recognized capture boundary.
        var tree = SyntaxTree.Parse("void main() { outColor = vec4(1.0); }", GlslSchema.Instance);

        // Act and assert: the rewrite reports the missing required boundary.
        Assert.Throws<InvalidOperationException>(() => PbrTerrainColorPatches.ApplyFragment(tree, "chunkopaque.fsh"));
    }

    /// <summary>Material restoration remains in scope when the source has no trailing glow output.</summary>
    [Fact]
    public void TrailingConditionalWithoutGlowRetainsMaterialScope()
    {
        // Arrange: provide a controlled conditional source without a final glow assignment.
        var tree = SyntaxTree.Parse("void main() { vec4 texColor=vec4(1); float murkiness=0;\n#if NORMALVIEW > 0\noutColor=texColor;\n#endif\n}", GlslSchema.Instance);

        // Act: apply the fragment capture rule to the inline source.
        PbrTerrainColorPatches.ApplyFragment(tree, "chunkopaque.fsh");
        string actual = tree.ToText();

        // Assert: capture and restore share a scope and restoration follows the conditional.
        Assert.Equal(ScopeAt(actual, "vec3 vge_materialColor ="), ScopeAt(actual, "outColor = vec4(vge_materialColor, 1.0);"));
        Assert.True(actual.LastIndexOf("outColor = vec4(vge_materialColor, 1.0);", StringComparison.Ordinal) > actual.IndexOf("#endif", StringComparison.Ordinal));
    }
    #endregion

    #region Private
    /// <summary>Returns the lexical brace identities enclosing a source marker, ignoring comments.</summary>
    private static int[] ScopeAt(string source, string marker)
    {
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
    #endregion
}
