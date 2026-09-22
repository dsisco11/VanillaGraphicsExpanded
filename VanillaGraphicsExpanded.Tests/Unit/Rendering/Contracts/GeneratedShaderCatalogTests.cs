using VanillaGraphicsExpanded.Rendering.Contracts;

namespace VanillaGraphicsExpanded.Tests.Unit.Rendering.Contracts;

/// <summary>Requires generated references to own the complete packaged catalog and its isolated scopes.</summary>
public sealed class GeneratedShaderCatalogTests
{
    #region Generated coverage
    /// <summary>Every packaged contract comes directly from the generated catalog, without a reflection fallback.</summary>
    [Fact]
    public void GeneratedCatalogContainsEveryPackagedProgram()
    {
        var generated = GeneratedShaderCatalog.Programs();
        Assert.Equal(63, generated.Count);
        Assert.Equal(GpuShaderContracts.Registry.Programs.Keys.Order(StringComparer.Ordinal), generated.Select(p => p.Identity).Order(StringComparer.Ordinal));
        Assert.All(generated, program => Assert.Same(program, GpuShaderContracts.Registry.FindProgram(program.Identity)));
        Assert.Equal(98, generated.SelectMany(p => p.Stages).Select(s => s.Identity).Distinct().Count());
        Assert.Equal(242, generated.Sum(p => p.Assignments.Count));
        Assert.Equal(243, new ShaderVariantResolver(generated).Binaries.Count);
    }

    /// <summary>Isolated fixtures stay explicit and cannot silently enter the packaged program set.</summary>
    [Fact]
    public void GeneratedScopesKeepFixturesSeparate()
    {
        var isolated = GeneratedShaderCatalog.Programs("build-validation");
        Assert.Equal(2, isolated.Count);
        Assert.All(isolated, program => Assert.DoesNotContain(GeneratedShaderCatalog.Programs(), p => p.Identity == program.Identity));
        Assert.Single(GeneratedShaderCatalog.Programs("generator-fixture"));
    }
    #endregion
}

