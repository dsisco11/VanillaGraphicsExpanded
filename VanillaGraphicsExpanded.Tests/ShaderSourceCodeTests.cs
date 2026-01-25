using System;
using System.Collections.Generic;

using TinyPreprocessor.Core;

using VanillaGraphicsExpanded;
using VanillaGraphicsExpanded.PBR;
using VanillaGraphicsExpanded.Tests.Helpers;

using Xunit;

namespace VanillaGraphicsExpanded.Tests;

public sealed class ShaderSourceCodeTests
{
    private readonly ITestOutputHelper _output;

    public ShaderSourceCodeTests(ITestOutputHelper output)
    {
        _output = output;
    }

    private static string NormalizeLineEndings(string text) => text.ReplaceLineEndings("\n");

    [Fact]
    public void FromSource_InjectsLineDirectives_WithCorrectLineNumbers_ForRootAndImports()
    {
        const string include =
            "// Include start\n" +
            "void Foo() { }\n";

        const string shader =
            "#version 330 core\n" +
            "// Root before\n" +
            "@import \"./includes/shared.glsl\"\n" +
            "// Root after\n" +
            "void main() { Foo(); }\n";

        var sources = new Dictionary<string, string>
        {
            ["shaders/includes/shared.glsl"] = include
        };

        var resolver = new DictionarySyntaxTreeResourceResolver(sources, ShaderImportsSystem.DefaultDomain);
        var preprocessor = new ShaderSyntaxTreePreprocessor(resolver);

        TinyTokenizer.Ast.SyntaxTree ContentProvider(ResourceId id)
        {
            string raw = id.ToString();
            string path = raw;
            int colon = raw.IndexOf(':');
            if (colon >= 0)
            {
                path = raw[(colon + 1)..];
            }

            if (path == "shaders/testshader.fsh")
            {
                return ShaderImportsSystem.Instance.CreateSyntaxTree(shader, "testshader.fsh")!;
            }

            return sources.TryGetValue(path, out var text)
                ? ShaderImportsSystem.Instance.CreateSyntaxTree(text, path)!
                : TinyTokenizer.Ast.SyntaxTree.Parse(string.Empty, GlslSchema.Instance);
        }

        var code = ShaderSourceCode.FromSource(
            shaderName: "testshader",
            stageExtension: "fsh",
            rawSource: shader,
            sourceName: "testshader.fsh",
            importPreprocessor: preprocessor,
            contentProvider: ContentProvider,
            ct: TestContext.Current.CancellationToken);

        var emitted = NormalizeLineEndings(code.EmittedSource);

        _output.WriteLine($"FinalTree.TextLength: {code.FinalTree.TextLength}");
        _output.WriteLine($"EmittedSourceUnstripped.Length: {code.EmittedSourceUnstripped.Length}");
        _output.WriteLine($"EmittedSource.Length: {code.EmittedSource.Length}");
        _output.WriteLine("EmittedSource:");
        _output.WriteLine(emitted);

        _output.WriteLine("LineDirectiveSourceIdToResource:");
        foreach (var kvp in code.LineDirectiveSourceIdToResource)
        {
            _output.WriteLine($"  {kvp.Key} -> {kvp.Value}");
        }

        if (code.ImportResult is not null)
        {
            var ranges = code.ImportResult.SourceMap.QueryRangeByEnd(0, code.FinalTree.TextLength);
            _output.WriteLine($"SourceMap ranges: {ranges.Count}");
            foreach (var range in ranges.Take(10))
            {
                _output.WriteLine(
                    $"  gen[{range.GeneratedStartOffset},{range.GeneratedEndOffset}) orig[{range.OriginalStartOffset},{range.OriginalEndOffset}) res={range.Resource}");
            }

            int idxInclude = emitted.IndexOf("// Include start", StringComparison.Ordinal);
            _output.WriteLine($"Index('// Include start') in emitted: {idxInclude}");

            // Heuristic: adjust for the injected #line directive line (present in emitted but not in SourceMap).
            int injectedLenGuess = "#line 1 1\n".Length;
            int preOffsetGuess = idxInclude >= 0 ? Math.Max(0, idxInclude - injectedLenGuess) : -1;
            _output.WriteLine($"SourceMap.Query({preOffsetGuess}) => {code.ImportResult.SourceMap.Query(preOffsetGuess)}");
        }

        Assert.Contains("void Foo()", emitted);
        Assert.DoesNotContain("@import", emitted);
        Assert.NotEmpty(code.LineDirectiveSourceIdToResource);

        static int FindSourceId(IReadOnlyDictionary<int, string> map, string suffix)
        {
            foreach (var (id, resource) in map)
            {
                if (resource.Replace('\\', '/').Contains(suffix, StringComparison.Ordinal))
                {
                    return id;
                }
            }
            throw new InvalidOperationException($"Source id not found for suffix '{suffix}'.");
        }

        int rootId = FindSourceId(code.LineDirectiveSourceIdToResource, "shaders/testshader.fsh");
        int includeId = FindSourceId(code.LineDirectiveSourceIdToResource, "shaders/includes/shared.glsl");

        // Root source mapping: after #version, the next content starts on line 2 in the original root source.
        Assert.Contains($"#line 2 {rootId}\n", emitted);

        // Included file should start at line 1 of that include.
        Assert.Contains($"#line 1 {includeId}\n", emitted);

        // After the import, we resume the root source at line 4 ("// Root after").
        Assert.Contains($"#line 4 {rootId}\n", emitted);
    }

    [Fact]
    public void FromSource_InlinesImports_AndProvidesSourceMap()
    {
        const string include = "// Shared code\nfloat PI = 3.14159;\n";

        const string shader = "#version 330 core\n" +
                              "@import \"./includes/shared.glsl\"\n" +
                              "void main() { }\n";

        var sources = new Dictionary<string, string>
        {
            ["shaders/includes/shared.glsl"] = include
        };

        var resolver = new DictionarySyntaxTreeResourceResolver(sources, ShaderImportsSystem.DefaultDomain);
        var preprocessor = new ShaderSyntaxTreePreprocessor(resolver);

        TinyTokenizer.Ast.SyntaxTree ContentProvider(ResourceId id)
        {
            // ResourceIds produced by the resolver look like `{domain}:{path}`.
            string raw = id.ToString();
            string path = raw;
            int colon = raw.IndexOf(':');
            if (colon >= 0)
            {
                path = raw[(colon + 1)..];
            }

            if (path == "shaders/testshader.fsh")
            {
                return ShaderImportsSystem.Instance.CreateSyntaxTree(shader, "testshader.fsh")!;
            }

            return sources.TryGetValue(path, out var text)
                ? ShaderImportsSystem.Instance.CreateSyntaxTree(text, path)!
                : TinyTokenizer.Ast.SyntaxTree.Parse(string.Empty, GlslSchema.Instance);
        }

        var code = ShaderSourceCode.FromSource(
            shaderName: "testshader",
            stageExtension: "fsh",
            rawSource: shader,
            sourceName: "testshader.fsh",
            importPreprocessor: preprocessor,
            contentProvider: ContentProvider,
            ct: TestContext.Current.CancellationToken);

        var emitted = NormalizeLineEndings(code.EmittedSource);

        Assert.Contains("float PI = 3.14159", emitted);
        Assert.DoesNotContain("@import", emitted);

        Assert.NotNull(code.ImportResult);

        // TinyPreprocessor exposes SourceMap as public API.
        Assert.NotNull(code.ImportResult!.SourceMap);

        // If #line injection ran, we should see directives and a non-empty id map.
        Assert.Contains("#line ", emitted);
        Assert.NotEmpty(code.LineDirectiveSourceIdToResource);
    }

    [Fact]
    public void FromSource_InsertsDefines_AfterVersionDirective()
    {
        const string shader = "#version 330 core\n" +
                              "void main() { }\n";

        var resolver = new DictionarySyntaxTreeResourceResolver(new Dictionary<string, string>(), ShaderImportsSystem.DefaultDomain);
        var preprocessor = new ShaderSyntaxTreePreprocessor(resolver);

        var defines = new Dictionary<string, string?>
        {
            ["FOO"] = "1",
            ["BAR"] = null
        };

        var code = ShaderSourceCode.FromSource(
            shaderName: "testshader",
            stageExtension: "fsh",
            rawSource: shader,
            sourceName: "testshader.fsh",
            defines: defines,
            importPreprocessor: preprocessor,
            ct: TestContext.Current.CancellationToken);

        var emitted = NormalizeLineEndings(code.EmittedSource);

        int idxVersion = emitted.IndexOf("#version 330 core\n", StringComparison.Ordinal);
        Assert.True(idxVersion >= 0);

        int idxDefineFoo = emitted.IndexOf("#define FOO 1\n", StringComparison.Ordinal);
        int idxDefineBar = emitted.IndexOf("#define BAR\n", StringComparison.Ordinal);
        Assert.True(idxDefineFoo > idxVersion);
        Assert.True(idxDefineBar > idxVersion);

        int idxMain = emitted.IndexOf("void main()", StringComparison.Ordinal);
        Assert.True(idxMain > idxDefineFoo);
        Assert.True(idxMain > idxDefineBar);
    }
}
