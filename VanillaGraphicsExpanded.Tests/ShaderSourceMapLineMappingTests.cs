using System;
using System.Collections.Generic;
using System.Linq;

using TinyPreprocessor.Core;
using TinyPreprocessor.Text;

using TinyAst.Preprocessor.Bridge.Content;

using TinyTokenizer.Ast;

using VanillaGraphicsExpanded.PBR;
using VanillaGraphicsExpanded.Tests.Helpers;

using Xunit;

namespace VanillaGraphicsExpanded.Tests;

public sealed class ShaderSourceMapLineMappingTests
{
    private static string NormalizeLineEndings(string text) => text.ReplaceLineEndings("\n");

    private static (int Line, int Column) ManualLineColumn(string text, int offset)
    {
        text = text ?? string.Empty;
        offset = Math.Clamp(offset, 0, text.Length);

        int line = 1;
        int column = 1;

        for (int i = 0; i < offset; i++)
        {
            if (text[i] == '\n')
            {
                line++;
                column = 1;
            }
            else
            {
                column++;
            }
        }

        return (line, column);
    }

    [Fact]
    public void SyntaxTreeLineColumnMapper_DoesNotMatchManualLineCounting_ForGlslSchema_Repro()
    {
        const string shader =
            "#version 330 core\n" +
            "@import \"./includes/shared.glsl\"\n" +
            "void main() { }\n";

        var tree = SyntaxTree.ParseAndBind(shader, GlslSchema.Instance);
        var resourceId = new ResourceId("game:shaders/testshader.fsh");

        string text = NormalizeLineEndings(tree.ToText());

        Assert.True(
            SyntaxTreeContentBoundaryResolverProvider.Instance.TryGet<SyntaxTree, LineBoundary>(out var boundaryResolver),
            "Expected SyntaxTreeContentBoundaryResolverProvider to provide a LineBoundary resolver for SyntaxTree.");

        // Pick an offset that is clearly on line 2.
        int idxFirstNewline = text.IndexOf('\n', StringComparison.Ordinal);
        int offsetLine2 = Math.Min(idxFirstNewline + 2, text.Length - 1); // "@"
        Assert.True(offsetLine2 > 0 && offsetLine2 < text.Length);

        bool ok = SyntaxTreeLineColumnMapper.TryGetLineColumnRange(
            tree,
            resourceId,
            offsetLine2..Math.Min(offsetLine2 + 1, tree.TextLength),
            boundaryResolver,
            out var start,
            out _);

        Assert.True(ok);

        // Manual line counting says this offset is on line 2.
        var expected = ManualLineColumn(text, offsetLine2);
        Assert.Equal(2, expected.Line);

        // Current observed behavior: mapper returns line 1.
        // This is the failure point that causes our #line injection to emit "#line 1 ..." after #version.
        Assert.Equal(1, start.Line);
    }

    [Fact]
    public void SyntaxTreeLineColumnMapper_LineStartBoundary_IsAnEdgeCase_Repro()
    {
        const string shader =
            "#version 330 core\n" +
            "@import \"./includes/shared.glsl\"\n" +
            "void main() { }\n";

        var tree = SyntaxTree.ParseAndBind(shader, GlslSchema.Instance);
        var resourceId = new ResourceId("game:shaders/testshader.fsh");

        string text = NormalizeLineEndings(tree.ToText());
        int boundaryOffset = text.IndexOf('\n', StringComparison.Ordinal) + 1; // start of line 2
        Assert.True(boundaryOffset > 0 && boundaryOffset < text.Length);

        Assert.True(
            SyntaxTreeContentBoundaryResolverProvider.Instance.TryGet<SyntaxTree, LineBoundary>(out var boundaryResolver),
            "Expected SyntaxTreeContentBoundaryResolverProvider to provide a LineBoundary resolver for SyntaxTree.");

        bool okAtBoundary = SyntaxTreeLineColumnMapper.TryGetLineColumnRange(
            tree,
            resourceId,
            boundaryOffset..Math.Min(boundaryOffset + 1, tree.TextLength),
            boundaryResolver,
            out var atBoundary,
            out _);

        bool okInsideLine = SyntaxTreeLineColumnMapper.TryGetLineColumnRange(
            tree,
            resourceId,
            (boundaryOffset + 1)..Math.Min(boundaryOffset + 2, tree.TextLength),
            boundaryResolver,
            out var insideLine,
            out _);

        Assert.True(okAtBoundary);
        Assert.True(okInsideLine);

        // This character is visually on line 2.
        // Manual line counting confirms that.
        var expectedAtBoundary = ManualLineColumn(text, boundaryOffset);
        Assert.Equal(2, expectedAtBoundary.Line);
        Assert.Equal(1, expectedAtBoundary.Column);

        // Current observed behavior: both the line-start boundary and an offset inside line 2 map to line 1.
        // This suggests the line-boundary resolver isn't detecting newlines for this SyntaxTree/schema.
        Assert.Equal(1, atBoundary.Line);
        Assert.Equal(1, insideLine.Line);

        // Confirm the boundary resolver doesn't see any line boundaries (expected to include the start offset of line 2).
        var boundaries = boundaryResolver.ResolveOffsets(tree, resourceId, 0, tree.TextLength).ToArray();
        Assert.DoesNotContain(boundaryOffset, boundaries);
    }

    [Fact]
    public void SourceMap_Query_Matches_QueryRangeByEnd_ForOffsetAfterVersion()
    {
        const string include = "void Foo() { }\n";

        const string shader =
            "#version 330 core\n" +
            "@import \"./includes/shared.glsl\"\n" +
            "void main() { Foo(); }\n";

        var sources = new Dictionary<string, string>
        {
            ["shaders/includes/shared.glsl"] = include
        };

        var resolver = new DictionarySyntaxTreeResourceResolver(sources, ShaderImportsSystem.DefaultDomain);
        var preprocessor = new ShaderSyntaxTreePreprocessor(resolver);

        var rootId = new ResourceId($"{ShaderImportsSystem.DefaultDomain}:shaders/testshader.fsh");
        var rootTree = SyntaxTree.ParseAndBind(shader, GlslSchema.Instance);

        var result = preprocessor.Process(rootId, rootTree, context: null, options: null, ct: TestContext.Current.CancellationToken);
        Assert.True(result.Success);

        string outputText = NormalizeLineEndings(result.Content.ToText());
        int generatedAfterVersion = outputText.IndexOf('\n', StringComparison.Ordinal) + 1;
        Assert.True(generatedAfterVersion > 0);

        var loc = result.SourceMap.Query(generatedAfterVersion);
        Assert.NotNull(loc);

        var ranges = result.SourceMap.QueryRangeByEnd(0, result.Content.TextLength);
        var range = ranges.Single(r => generatedAfterVersion >= r.GeneratedStartOffset && generatedAfterVersion < r.GeneratedEndOffset);

        Assert.Equal(range.Resource, loc!.Resource);

        int derivedOriginalOffset = range.OriginalStartOffset + (generatedAfterVersion - range.GeneratedStartOffset);
        Assert.Equal(derivedOriginalOffset, loc.OriginalOffset);
    }

    [Fact]
    public void SourceMap_Query_OffsetAfterVersion_MapsToLineColumn_ConsistentWithManualCounting()
    {
        const string include = "void Foo() { }\n";

        const string shader =
            "#version 330 core\n" +
            "@import \"./includes/shared.glsl\"\n" +
            "void main() { Foo(); }\n";

        var sources = new Dictionary<string, string>
        {
            ["shaders/includes/shared.glsl"] = include
        };

        var resolver = new DictionarySyntaxTreeResourceResolver(sources, ShaderImportsSystem.DefaultDomain);
        var preprocessor = new ShaderSyntaxTreePreprocessor(resolver);

        var rootId = new ResourceId($"{ShaderImportsSystem.DefaultDomain}:shaders/testshader.fsh");
        var rootTree = SyntaxTree.ParseAndBind(shader, GlslSchema.Instance);

        var result = preprocessor.Process(rootId, rootTree, context: null, options: null, ct: TestContext.Current.CancellationToken);
        Assert.True(result.Success);

        string outputText = NormalizeLineEndings(result.Content.ToText());
        int generatedAfterVersion = outputText.IndexOf('\n', StringComparison.Ordinal) + 1;
        Assert.True(generatedAfterVersion > 0);

        var loc = result.SourceMap.Query(generatedAfterVersion);
        Assert.NotNull(loc);

        string rawId = loc!.Resource.ToString();
        string path = rawId;
        int colon = rawId.IndexOf(':');
        if (colon >= 0)
        {
            path = rawId[(colon + 1)..];
        }

        SyntaxTree content;
        if (string.Equals(path, "shaders/testshader.fsh", StringComparison.Ordinal))
        {
            content = rootTree;
        }
        else if (sources.TryGetValue(path, out var text))
        {
            content = SyntaxTree.ParseAndBind(text, GlslSchema.Instance);
        }
        else
        {
            content = SyntaxTree.ParseAndBind(string.Empty, GlslSchema.Instance);
        }

        Assert.True(
            SyntaxTreeContentBoundaryResolverProvider.Instance.TryGet<SyntaxTree, LineBoundary>(out var boundaryResolver),
            "Expected SyntaxTreeContentBoundaryResolverProvider to provide a LineBoundary resolver for SyntaxTree.");

        int originalOffset = Math.Clamp(loc.OriginalOffset, 0, Math.Max(0, content.TextLength - 1));
        bool ok = SyntaxTreeLineColumnMapper.TryGetLineColumnRange(
            content,
            loc.Resource,
            originalOffset..Math.Min(originalOffset + 1, content.TextLength),
            boundaryResolver,
            out var start,
            out _);

        Assert.True(ok);

        string contentText = NormalizeLineEndings(content.ToText());
        var expected = ManualLineColumn(contentText, originalOffset);
        Assert.Equal(expected.Line, start.Line);
        Assert.Equal(expected.Column, start.Column);
    }

    [Fact(Skip = "Upstream/behavioral gap: SourceMap does not consistently expose per-import segments yet; keep as multi-import repro for later.")]
    public void SourceMap_WithMultipleImports_MapsGeneratedOffsetsToCorrectResources()
    {
        const string includeA =
            "// A_START\n" +
            "@import \"./nested/c.glsl\"\n" +
            "void FromA() { }\n" +
            "// A_END\n";

        const string includeB =
            "// B_START\n" +
            "void FromB() { }\n" +
            "// B_END\n";

        const string includeC =
            "// C_START\n" +
            "void FromC() { }\n" +
            "// C_END\n";

        const string shader =
            "#version 330 core\n" +
            "@import \"./includes/a.glsl\"\n" +
            "@import \"./includes/b.glsl\"\n" +
            "void main() { FromA(); FromB(); FromC(); }\n";

        var sources = new Dictionary<string, string>
        {
            ["shaders/includes/a.glsl"] = includeA,
            ["shaders/includes/b.glsl"] = includeB,
            ["shaders/includes/nested/c.glsl"] = includeC
        };

        var resolver = new DictionarySyntaxTreeResourceResolver(sources, ShaderImportsSystem.DefaultDomain);
        var preprocessor = new ShaderSyntaxTreePreprocessor(resolver);

        var rootId = new ResourceId($"{ShaderImportsSystem.DefaultDomain}:shaders/testshader.fsh");
        var rootTree = SyntaxTree.ParseAndBind(shader, GlslSchema.Instance);

        var result = preprocessor.Process(rootId, rootTree, context: null, options: null, ct: TestContext.Current.CancellationToken);
        Assert.True(result.Success);

        string output = NormalizeLineEndings(result.Content.ToText());

        static int FindOrThrow(string haystack, string needle)
        {
            int idx = haystack.IndexOf(needle, StringComparison.Ordinal);
            Assert.True(idx >= 0, $"needle not found: {needle}");
            return idx;
        }

        // Pick offsets inside unique tokens in each imported file.
        int offsetA = FindOrThrow(output, "A_START");
        int offsetB = FindOrThrow(output, "B_START");
        int offsetC = FindOrThrow(output, "C_START");

        var locA = result.SourceMap.Query(offsetA);
        var locB = result.SourceMap.Query(offsetB);
        var locC = result.SourceMap.Query(offsetC);

        Assert.NotNull(locA);
        Assert.NotNull(locB);
        Assert.NotNull(locC);

        Assert.Contains("shaders/includes/a.glsl", locA!.Resource.ToString().Replace('\\', '/'), StringComparison.Ordinal);
        Assert.Contains("shaders/includes/b.glsl", locB!.Resource.ToString().Replace('\\', '/'), StringComparison.Ordinal);
        Assert.Contains("shaders/includes/nested/c.glsl", locC!.Resource.ToString().Replace('\\', '/'), StringComparison.Ordinal);
    }
}
