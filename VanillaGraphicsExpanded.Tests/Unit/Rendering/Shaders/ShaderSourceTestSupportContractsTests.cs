using System.Collections.Generic;
using TinyPreprocessor.Core;
using TinyTokenizer.Ast;
using VanillaGraphicsExpanded.Tests.Support.Rendering.Shaders;
using VanillaGraphicsExpanded.PBR;
using Xunit;

namespace VanillaGraphicsExpanded.Tests.Unit.Rendering.Shaders;

/// <summary>Verifies the dictionary resource resolver used by shader source tests.</summary>
public sealed class ShaderSourceTestSupportContractsTests
{
    #region Public API
    /// <summary>Resolves a relative shader import against its parent resource.</summary>
    [Fact]
    public async Task DictionaryResolver_ResolvesBareImportReference()
    {
        // Arrange
        var sources = new Dictionary<string, string>
        {
            ["shaders/includes/shared.glsl"] = ShaderPatchingTestSources.SharedInclude
        };

        var resolver = new DictionarySyntaxTreeResourceResolver(sources, ShaderImportsSystem.DefaultDomain);
        var baseResource = new Resource<SyntaxTree>(
            new ResourceId($"{ShaderImportsSystem.DefaultDomain}:shaders/test.fsh"),
            SyntaxTree.Parse("", GlslSchema.Instance),
            new Dictionary<string, object>());
        // Act
        var result = await resolver.ResolveAsync(
            "./includes/shared.glsl",
            relativeTo: baseResource,
            ct: TestContext.Current.CancellationToken);

        // Assert
        Assert.True(result.IsSuccess, result.Error?.ToString());
        Assert.NotNull(result.Resource);
        Assert.Equal($"{ShaderImportsSystem.DefaultDomain}:shaders/includes/shared.glsl", result.Resource.Id.Path);
    }
    #endregion
}
