using TinyTokenizer.Ast;
using VanillaGraphicsExpanded.PBR;

namespace VanillaGraphicsExpanded.Tests.Integration.Terrain;

/// <summary>Verifies material capture rewrites against installed engine shader assets.</summary>
public sealed class PbrTerrainInstalledShaderTests
{
    #region Public API
    /// <summary>The complete production patch chain keeps capture and restoration in one executable scope.</summary>
    [Theory]
    [InlineData("chunkopaque.fsh")]
    [InlineData("chunktopsoil.fsh")]
    public void ProductionPatchChainRetainsMaterialScope(string name)
    {
        // Arrange: parse the installed engine shader through its production schema.
        var tree = SyntaxTree.Parse(ReadShader(name), GlslSchema.Instance);

        // Act: apply the complete production shader patch chain.
        bool applied = VanillaShaderPatches.TryApplyPatches(null, tree, name);
        string actual = tree.ToText();

        // Assert: capture and restored output remain in the same executable scope.
        Assert.True(applied);
        Assert.Equal(ScopeAt(actual, "vec3 vge_materialColor ="), ScopeAt(actual, "outColor = vec4(vge_materialColor, 1.0);"));
    }

    /// <summary>Fragment capture follows color mapping and precedes forward effects without changing the remaining source.</summary>
    [Theory]
    [InlineData("chunkopaque.fsh", "texColor.rgb")]
    [InlineData("chunktopsoil.fsh", "outColor.rgb")]
    public void FragmentCapturePreservesBodyAndCapturesBeforeEffects(string name, string color)
    {
        // Arrange: retain the installed shader's exact normalized source and parse it.
        string source = ReadShader(name).ReplaceLineEndings("\n");
        var tree = SyntaxTree.Parse(source, GlslSchema.Instance);
        string before = tree.ToText();
        Assert.Equal(source, before);

        // Act: apply only the production fragment capture transformation.
        PbrTerrainColorPatches.ApplyFragment(tree, name);
        string after = tree.ToText().ReplaceLineEndings("\n");
        string capture = $"vec3 vge_materialColor = VgeSrgbToLinear({color});\n";
        const string restore = "\n#if NORMALVIEW == 0\n    // Coverage has already been resolved by the engine discard tests.\n    // Deferred albedo must replace background radiance, even if interpolated alpha is just below one.\n    outColor = vec4(vge_materialColor, 1.0);\n#endif\n";

        // Assert: capture ordering, scope, and exact preservation of the unmodified body are maintained.
        Assert.Contains(capture, after);
        Assert.True(after.IndexOf(capture, StringComparison.Ordinal) < after.IndexOf("float murkiness", StringComparison.Ordinal));
        Assert.Equal(ScopeAt(after, "vec3 vge_materialColor ="), ScopeAt(after, "outColor = vec4(vge_materialColor, 1.0);"));
        Assert.Equal(before, after.Replace(capture, "", StringComparison.Ordinal).Replace(restore, "", StringComparison.Ordinal));
    }

    /// <summary>Vertex neutralization changes only RGB while preserving engine alpha and side outputs.</summary>
    [Theory]
    [InlineData("chunkopaque.vsh")]
    [InlineData("chunktopsoil.vsh")]
    public void VertexCapturePreservesOriginalBody(string name)
    {
        // Arrange: retain the parsed installed engine shader before transformation.
        var tree = SyntaxTree.Parse(ReadShader(name), GlslSchema.Instance);
        string before = tree.ToText();

        // Act: apply the production vertex capture transformation.
        PbrTerrainColorPatches.ApplyVertex(tree, name);
        string actual = tree.ToText().ReplaceLineEndings("\n");
        const string insertion = "\n    // Deferred lighting consumes unlit material RGB; retain the engine fade alpha.\n    rgba.rgb = vec3(1.0);\n    float vge_skyAvailability = VgeSkyAvailability(rgbaLightIn.a, false);\n    // Interpolate the engine field before the fragment applies the nonlinear curve.\n    vge_terrainLight = vec4(rgbaLightIn.rgb, vge_skyAvailability);\n";

        // Assert: required capture fields appear and removing them recovers the original body.
        Assert.Contains(insertion, actual);
        Assert.Equal(before.ReplaceLineEndings("\n"), actual.Replace(insertion, "", StringComparison.Ordinal).Replace("\nout vec4 vge_terrainLight;\n", "", StringComparison.Ordinal));
    }

    /// <summary>Transparent and liquid installed shaders remain unchanged by deferred material capture.</summary>
    [Theory]
    [InlineData("chunktransparent.fsh")]
    [InlineData("chunkliquid.fsh")]
    public void UnsupportedMaterialPathsRemainUnchanged(string name)
    {
        // Arrange: read and parse each installed shader that is outside the material-capture path.
        var tree = SyntaxTree.Parse(ReadShader(name), GlslSchema.Instance);
        string before = tree.ToText();

        // Act: ask the capture rule to process the unsupported material shader.
        PbrTerrainColorPatches.ApplyFragment(tree, name);

        // Assert: unsupported source stays byte-for-byte equivalent at the parsed-source boundary.
        Assert.Equal(before, tree.ToText());
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

    /// <summary>Reads one shader from the installed engine asset tree used by the project references.</summary>
    private static string ReadShader(string name)
    {
        string root = Environment.GetEnvironmentVariable("VINTAGE_STORY")
            ?? throw new InvalidOperationException("VINTAGE_STORY must identify the test engine installation.");
        return TestAssetSnapshot.ReadSourceText(Path.Combine(root, "assets", "game", "shaders", name));
    }
    #endregion
}
