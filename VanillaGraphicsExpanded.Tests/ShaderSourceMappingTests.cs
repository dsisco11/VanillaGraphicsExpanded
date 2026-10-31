using VanillaGraphicsExpanded.Tests.GPU.Helpers;
using TinyPreprocessor.Core;
using TinyTokenizer.Ast;
using VanillaGraphicsExpanded.PBR;

namespace VanillaGraphicsExpanded.Tests;

/// <summary>Checks shader preprocessing without creating a graphics context.</summary>
[Collection("CPU state")]
public sealed class ShaderSourceMappingTests
{
    #region Public API
    /// <summary>Checks define placement through production preprocessing without native compilation.</summary>
    [Fact]
    public void ProcessedSource_InsertsDefinesImmediatelyAfterVersion()
    {


        var shaderPath = Path.Combine(AppContext.BaseDirectory, "assets", "shaders");
        var includePath = Path.Combine(AppContext.BaseDirectory, "assets", "shaders", "includes");

        Assert.True(TestAssetSnapshot.HasDirectory(shaderPath), $"Missing built shader input: {shaderPath}");
        Assert.True(TestAssetSnapshot.HasDirectory(includePath), $"Missing built include input: {includePath}");

        using var helper = new ShaderTestHelper();

        var defines = new Dictionary<string, string?>
        {
            ["VGE_TEST_DEFINE"] = "1",
            ["VGE_TEST_FLOAT"] = "2.0",
        };

        var source = helper.GetProcessedSource("lumon_probe_atlas_gather.fsh", defines);
        Assert.NotNull(source);

        int versionIndex = source!.IndexOf("#version", StringComparison.Ordinal);
        Assert.True(versionIndex >= 0, "Expected processed source to contain #version");

        int versionLineEnd = source.IndexOf('\n', versionIndex);
        Assert.True(versionLineEnd >= 0, "Expected #version to be terminated by a newline");

        // Defines must be injected immediately after the #version line.
        int injectedStart = versionLineEnd + 1;
        Assert.True(source.AsSpan(injectedStart).StartsWith("#define VGE_TEST_DEFINE 1\n", StringComparison.Ordinal),
            "Expected injected defines to start immediately after #version line");
    }


    /// <summary>Preserves imported source identities and compiler line mappings using immutable build inputs.</summary>
    [Fact]
    public void DebugEntrypoint_Preprocess_EmitsLineDirectives_ForAccurateErrorLocations()
    {
        // Build-owned inputs are frozen once; source identities retain their relative include paths.
        const string filename = "lumon_debug_view_world_probe_irradiance_combined.fsh";
        var snapshot = TestAssetSnapshot.Built;
        string rawSource = snapshot.Text("shaders/" + filename);
        var sources = snapshot.Paths.Where(path => path.StartsWith("shaders/includes/", StringComparison.Ordinal)
            && Path.GetExtension(path) is ".glsl" or ".fsh" or ".vsh")
            .ToDictionary(path => path, snapshot.Text, StringComparer.OrdinalIgnoreCase);
        Assert.NotEmpty(sources);
        var resolver = new DictionarySyntaxTreeResourceResolver(sources, ShaderImportsSystem.DefaultDomain);
        var preprocessor = new ShaderSyntaxTreePreprocessor(resolver);

        var cache = new Dictionary<ResourceId, SyntaxTree>();
        /// <summary>Resolves original relative import identities from the immutable input snapshot.</summary>
        SyntaxTree ContentProvider(ResourceId id)
        {
            if (cache.TryGetValue(id, out var cached))
            {
                return cached;
            }

            string idText = id.ToString();
            string path = idText;
            int colon = idText.IndexOf(':');
            if (colon >= 0)
            {
                path = idText[(colon + 1)..];
            }

            // Root shader stage.
            if (string.Equals(path, $"shaders/{filename}", StringComparison.Ordinal))
            {
                var tree = ShaderImportsSystem.Instance.CreateSyntaxTree(rawSource, filename)
                           ?? SyntaxTree.Parse(string.Empty, GlslSchema.Instance);
                cache[id] = tree;
                return tree;
            }

            // Imported resources.
            if (sources.TryGetValue(path, out var text))
            {
                var tree = ShaderImportsSystem.Instance.CreateSyntaxTree(text, path)
                           ?? SyntaxTree.Parse(string.Empty, GlslSchema.Instance);
                cache[id] = tree;
                return tree;
            }

            var empty = SyntaxTree.Parse(string.Empty, GlslSchema.Instance);
            cache[id] = empty;
            return empty;
        }

        var code = ShaderSourceCode.FromSource(
            shaderName: "lumon_debug_view_world_probe_irradiance_combined",
            stageExtension: "fsh",
            rawSource: rawSource,
            sourceName: filename,
            importPreprocessor: preprocessor,
            contentProvider: ContentProvider,
            ct: TestContext.Current.CancellationToken);

        Assert.Contains("#line ", code.EmittedSource);
        Assert.NotEmpty(code.LineDirectiveSourceIdToResource);
    }

    #endregion
}
