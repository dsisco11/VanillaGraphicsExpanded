using ShaderBuildTool.Spirv;
using TinyPreprocessor.Core;
using TinyPreprocessor.Graph;
using TinyTokenizer.Ast;
using VanillaGraphicsExpanded;
using ShaderBuildTool.Tests.Support.Generation;

namespace ShaderBuildTool.Tests.Component.Spirv.Compiler;

/// <summary>Checks graph membership and queries for an isolated root processed by the installed bridge.</summary>
public sealed class ShaderDependencyGraphTests
{
    #region Public API

    /// <summary>An isolated root has graph membership even though the graph ordering omits it.</summary>
    [Fact]
    public void IsolatedRootRequiresMembershipBeyondGraphProcessingOrder()
    {
        using var fixture = new ShaderBuildFixture();
        var root = new ResourceId("vanillagraphicsexpanded:shaders/isolated.fsh");
        var result = new ShaderSyntaxTreePreprocessor(
            new FileSystemSyntaxTreeResourceResolver(fixture.Assets, "vanillagraphicsexpanded"))
            .Process(root, SyntaxTree.Parse("void main() {}\n", GlslSchema.Instance), ct: TestContext.Current.CancellationToken);

        Assert.True(result.Success);
        Assert.NotNull(result.DependencyGraph);
        Assert.Equal(new[] { root }, result.DependencyGraph.GetAllResources());
        // The installed graph orders edges only; snapshots must enumerate all resources separately.
        Assert.Empty(result.DependencyGraph.GetProcessingOrder());
        Assert.Equal(new[] { root }, result.ProcessedResources);
        Assert.Empty(result.DependencyGraph.GetDependencies(root));
        Assert.Empty(result.DependencyGraph.GetDependents(root));
    }

    #endregion
}
