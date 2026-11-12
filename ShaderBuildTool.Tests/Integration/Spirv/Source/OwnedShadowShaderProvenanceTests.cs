using ShaderBuildTool.Spirv;
using ShaderBuildTool.Tests.Support.Generation;

namespace ShaderBuildTool.Tests.Integration.Spirv.Source;

/// <summary>Verifies packaged shadow shader expansion depends only on authored VGE assets.</summary>
[CollectionDefinition(nameof(OwnedShadowShaderCollection), DisableParallelization = true)]
public sealed class OwnedShadowShaderCollection { }

/// <summary>Checks VGE-owned shadow sources, shared deformation, cutout order and installation independence.</summary>
[Collection(nameof(OwnedShadowShaderCollection))]
public sealed class OwnedShadowShaderProvenanceTests
{
    private const string Domain = "vanillagraphicsexpanded";
    private static readonly string[] Stages =
    [
        "shadows/terrain.vsh", "shadows/terrain.fsh", "shadows/terrain-adaptive.vsh",
        "shadows/terrain-adaptive.tcsh", "shadows/terrain-adaptive.tesh",
        "shadows/entity.vsh", "shadows/entity.fsh", "shadows/static.vsh"
    ];

    #region Public API
    /// <summary>Every packaged shadow stage expands from VGE assets without reading a game installation.</summary>
    [Fact]
    public void ShadowStageGraphsContainOnlyVgeOwnedInputs()
    {
        // Arrange: choose a nonexistent installation so accidental game shader imports fail at the real resolver boundary.
        using var fixture = new ShaderBuildFixture();
        string previous = Environment.GetEnvironmentVariable("VINTAGE_STORY") ?? string.Empty;
        string missingInstallation = Path.Combine(fixture.Root, "no-game-installation");
        string assets = Path.Combine(fixture.Repository, "VanillaGraphicsExpanded", "assets");

        try
        {
            Environment.SetEnvironmentVariable("VINTAGE_STORY", missingInstallation);
            var processor = new ShaderSourcePreprocessor(assets, Domain);

            // Act: expand each packaged shadow root through its real parser and import resolver.
            var sources = Stages.Select(processor.Expand).ToArray();

            // Assert: source provenance, not just emitted text, remains inside this mod's packaged assets.
            foreach (var source in sources)
            {
                Assert.All(source.Graph.Resources, resource => Assert.StartsWith(Domain + ":", resource, StringComparison.Ordinal));
                Assert.All(source.Inputs, input => Assert.StartsWith(Domain + ":", input.Resource, StringComparison.Ordinal));
                Assert.All(source.Inputs, input => Assert.StartsWith(assets + Path.DirectorySeparatorChar, input.Path,
                    StringComparison.OrdinalIgnoreCase));
                Assert.DoesNotContain("game:", source.Text, StringComparison.OrdinalIgnoreCase);
            }
        }
        finally
        {
            Environment.SetEnvironmentVariable("VINTAGE_STORY", string.IsNullOrEmpty(previous) ? null : previous);
        }
    }

    /// <summary>Terrain, static, and skinned entity producers call the same owned bounded deformation kernel.</summary>
    [Fact]
    public void ShadowCastersShareOwnedDeformationAfterTheirNativeLayoutTransforms()
    {
        // Arrange: preprocess the three material families and their shared kernel from production asset roots.
        using var fixture = new ShaderBuildFixture();
        string assets = Path.Combine(fixture.Repository, "VanillaGraphicsExpanded", "assets");
        var processor = new ShaderSourcePreprocessor(assets, Domain);

        // Act: expand generated vertices and the common authored deformation source.
        string terrain = processor.Expand("shadows/terrain.vsh").Text;
        string entity = processor.Expand("shadows/entity.vsh").Text;
        string item = processor.Expand("shadows/static.vsh").Text;
        string kernel = File.ReadAllText(Path.Combine(assets, Domain, "shaders", "includes", "deformation", "vertex-deformation.glsl"));

        // Assert: each geometry family reaches the shared functions after supplying its own material flags and position.
        Assert.Contains("VgeVertexDeform", terrain, StringComparison.Ordinal);
        Assert.Contains("VgeGlobalDeform", terrain, StringComparison.Ordinal);
        Assert.Contains("VgeVertexDeform", entity, StringComparison.Ordinal);
        Assert.Contains("VgeGlobalDeform", entity, StringComparison.Ordinal);
        Assert.Contains("VgeVertexDeform", item, StringComparison.Ordinal);
        Assert.Contains("VgeGlobalDeform", item, StringComparison.Ordinal);
        Assert.Contains("vge_shadowAnimation.jointTransforms[clamp(vge_jointIndex, 0, 229)]", entity, StringComparison.Ordinal);
        Assert.Contains("shadowInstanceMatrix", item, StringComparison.Ordinal);
        Assert.Contains("mode = int((uint(flags) >> 25u) & 15u)", kernel, StringComparison.Ordinal);
        Assert.Contains("(uint(flags) >> 29u) & 7u", kernel, StringComparison.Ordinal);
        Assert.Contains("VgeLiquidDeform", kernel, StringComparison.Ordinal);
        Assert.Contains("VgePerceptionDeform", kernel, StringComparison.Ordinal);
    }

    /// <summary>Changing the installed game root cannot alter source records or cache reuse for owned shadow variants.</summary>
    [Fact]
    public void ShadowSourceCacheIdentityIsIndependentOfGameInstallation()
    {
        // Arrange: capture one complete expanded stage and persist its exact VGE dependency graph.
        using var fixture = new ShaderBuildFixture();
        string previous = Environment.GetEnvironmentVariable("VINTAGE_STORY") ?? string.Empty;
        string assets = Path.Combine(fixture.Repository, "VanillaGraphicsExpanded", "assets");
        var processor = new ShaderSourcePreprocessor(assets, Domain);
        var cache = new ShaderExpandedSourceCache(fixture.Output);
        var first = processor.Expand("shadows/entity.vsh");
        cache.Store(first, "owned-shadow-preprocessing");

        try
        {
            // Act: point at a different, nonexistent game root and re-expand the same owned source.
            Environment.SetEnvironmentVariable("VINTAGE_STORY", Path.Combine(fixture.Root, "another-game-root"));
            var second = processor.Expand("shadows/entity.vsh");

            // Assert: installation changes do not enter source identities, graph edges, or cache admission.
            Assert.Equal(first.Text, second.Text);
            Assert.Equal(first.Graph.Resources.ToArray(), second.Graph.Resources.ToArray());
            Assert.Equal(first.Graph.Edges.Select(edge => $"{edge.Dependent}>{edge.Dependency}").ToArray(),
                second.Graph.Edges.Select(edge => $"{edge.Dependent}>{edge.Dependency}").ToArray());
            Assert.Equal(first.Inputs.Select(input => $"{input.Resource}|{input.Path}|{input.Hash}").ToArray(),
                second.Inputs.Select(input => $"{input.Resource}|{input.Path}|{input.Hash}").ToArray());
            Assert.True(cache.TryRead(assets, first.Root, "owned-shadow-preprocessing",
                new ShaderFileHashIndex(fixture.Output, true), out var restored));
            Assert.Equal(first.Text, restored.Text);
        }
        finally
        {
            Environment.SetEnvironmentVariable("VINTAGE_STORY", string.IsNullOrEmpty(previous) ? null : previous);
        }
    }

    /// <summary>Shared alpha rejection remains before the VSM-only atomic depth write in both terrain and entity stages.</summary>
    [Fact]
    public void OwnedCutoutDiscardsBeforeAtomicShadowPublication()
    {
        // Arrange: preprocess the authored terrain and entity material fragments.
        using var fixture = new ShaderBuildFixture();
        string assets = Path.Combine(fixture.Repository, "VanillaGraphicsExpanded", "assets");
        var processor = new ShaderSourcePreprocessor(assets, Domain);

        // Act: expand each fragment through the same production import and stage-generation path.
        string terrain = processor.Expand("shadows/terrain.fsh").Text;
        string entity = processor.Expand("shadows/entity.fsh").Text;

        // Assert: each alpha discard precedes the material body and its guarded atomic depth contribution.
        foreach (string fragment in new[] { terrain, entity })
        {
            int discard = fragment.IndexOf("discard", StringComparison.Ordinal);
            int material = fragment.IndexOf("vgeShadowMaterialMain();", StringComparison.Ordinal);
            int atomic = fragment.IndexOf("shadowAtomicDepth(view,", StringComparison.Ordinal);
            Assert.True(discard >= 0 && material > discard && atomic > material);
        }
        Assert.Contains("texture(vge_shadowAtlas, vge_shadowUv).a", terrain, StringComparison.Ordinal);
        Assert.Contains("texture(vge_shadowAtlas, vge_shadowUv).a", entity, StringComparison.Ordinal);
    }
    #endregion
}
