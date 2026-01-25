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
    private static string NormalizeLineEndings(string text) => text.ReplaceLineEndings("\n");

    [Fact]
    public void FromSource_InjectsLineDirectives_UsesCorrectLineNumbers_ForRoot_AfterVersionDirective()
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

        TinyTokenizer.Ast.SyntaxTree ContentProvider(ResourceId id)
        {
            string raw = id.Path;
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

        Assert.Contains($"#line 2 {rootId}\n", emitted);
    }

    // Stable repro for upstream issue:
    // In current TinyPreprocessor versions, our SourceMap sometimes reports a single range mapped to the root resource
    // even when @import content has been inlined. That prevents correct #line switching to imported resources.
    [Fact(Skip = "Upstream: TinyPreprocessor SourceMap does not include imported resource segments in this scenario; keep as repro to report.")]
    public void FromSource_SourceMap_ShouldIncludeImportedResourceSegments()
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

        TinyTokenizer.Ast.SyntaxTree ContentProvider(ResourceId id)
        {
            string raw = id.Path;
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

        Assert.NotNull(code.ImportResult);

        // Expected: SourceMap contains a segment for the imported resource id.
        var ranges = code.ImportResult!.SourceMap.QueryRangeByEnd(0, code.FinalTree.TextLength);
        Assert.Contains(ranges, r => r.Resource.Path.Replace('\\', '/').Contains("shaders/includes/shared.glsl", StringComparison.Ordinal));
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
            string raw = id.Path;
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
