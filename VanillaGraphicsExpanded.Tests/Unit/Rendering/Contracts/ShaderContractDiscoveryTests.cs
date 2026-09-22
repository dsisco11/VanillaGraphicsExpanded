using VanillaGraphicsExpanded.Rendering.Contracts;
using static VanillaGraphicsExpanded.Tests.Unit.Rendering.Contracts.ShaderContractFixture;

namespace VanillaGraphicsExpanded.Tests.Unit.Rendering.Contracts;

/// <summary>Tests automatic contract discovery without registering fixture owners in production code.</summary>
public sealed class ShaderContractDiscoveryTests
{
    #region Discovery behavior
    /// <summary>Both property and field owners are discovered; auto-property backing fields are not duplicates.</summary>
    [Fact]
    public void DiscoversShaderOwnedInstancesWithoutRegistration()
    {
        var declarations = ShaderContractDiscovery.Discover([typeof(FieldOwner), typeof(PropertyOwner)]);
        Assert.Equal(2, declarations.Count);
        Assert.Contains(PropertyOwner.Contract, declarations);
        Assert.Contains(FieldOwner.Contract, declarations);
        Assert.Same(PropertyOwner.Contract, declarations.Single(c => c.Identity == "property"));
    }

    /// <summary>Owner ordering does not affect discovery, and collections are not additional declarations.</summary>
    [Fact]
    public void EnumerationIsDeterministicAndIgnoresHelpers()
    {
        var forward = ShaderContractDiscovery.Discover([typeof(PropertyOwner), typeof(FieldOwner), typeof(UnrelatedOwner)]);
        var reverse = ShaderContractDiscovery.Discover([typeof(UnrelatedOwner), typeof(FieldOwner), typeof(PropertyOwner)]);
        Assert.Equal(forward, reverse);
        Assert.Equal(2, forward.Count);
    }

    /// <summary>Inherited static members are discovered only at their declaring owner.</summary>
    [Fact]
    public void InheritedPropertiesDoNotDuplicateDeclarations()
    {
        var contracts = ShaderContractDiscovery.Discover([typeof(PropertyOwner), typeof(DerivedOwner)]);
        Assert.Same(PropertyOwner.Contract, Assert.Single(contracts));
    }

    /// <summary>Explicitly excluded validation owners are never initialized by packaged discovery.</summary>
    [Fact]
    public void ExcludedOwnersAreNotRead() =>
        Assert.Empty(ShaderContractDiscovery.Discover([typeof(ExcludedOwner)]));

    /// <summary>Invalid declarations fail with the owning member rather than silently disappearing.</summary>
    [Theory]
    [InlineData(typeof(MutableOwner))]
    [InlineData(typeof(MutableFieldOwner))]
    [InlineData(typeof(NullOwner))]
    [InlineData(typeof(ThrowingOwner))]
    [InlineData(typeof(GenericOwner<>))]
    public void InvalidDeclarationsIdentifyTheirOwner(Type owner)
    {
        var error = Assert.Throws<InvalidOperationException>(() => ShaderContractDiscovery.Discover([owner]));
        Assert.Contains(owner.FullName!, error.Message);
        Assert.Contains("Contract", error.Message);
    }
    #endregion

    #region Fixture owners
    /// <summary>A shader with an automatically backed immutable contract property.</summary>
    private class PropertyOwner
    {
        public static GpuShaderContract Contract { get; } = Declaration("property");
        public static IReadOnlyList<GpuShaderContract> Contracts => [Contract];
    }

    /// <summary>An inherited owner that contributes no new declaration.</summary>
    private sealed class DerivedOwner : PropertyOwner { }

    /// <summary>A shader exposing its declaration as a readonly field.</summary>
    private static class FieldOwner
    {
        public static readonly GpuShaderContract Contract = Declaration("field");
    }

    /// <summary>Unrelated static values must never be evaluated.</summary>
    private static class UnrelatedOwner
    {
        public static string Value => throw new InvalidOperationException("Not a contract.");
    }

    /// <summary>A validation-only declaration whose getter must remain untouched.</summary>
    [ExcludeFromShaderCatalog]
    private static class ExcludedOwner
    {
        public static GpuShaderContract Contract => throw new InvalidOperationException("Excluded.");
    }

    /// <summary>A writable contract property is not an immutable declaration.</summary>
    private static class MutableOwner
    {
        public static GpuShaderContract Contract { get; set; } = null!;
    }

    /// <summary>A writable contract field is not an immutable declaration.</summary>
    private static class MutableFieldOwner
    {
        public static GpuShaderContract Contract = null!;
    }

    /// <summary>A getter must provide a real declaration.</summary>
    private static class NullOwner
    {
        public static GpuShaderContract Contract => null!;
    }

    /// <summary>A failing initializer must surface its owner in diagnostics.</summary>
    private static class ThrowingOwner
    {
        public static GpuShaderContract Contract => throw new InvalidOperationException("Fixture failure.");
    }

    /// <summary>An open generic class cannot expose a single concrete shader declaration.</summary>
    private static class GenericOwner<T>
    {
        public static GpuShaderContract Contract => null!;
    }

    /// <summary>Creates a bounded declaration independent of GL and the production source catalog.</summary>
    private static GpuShaderContract Declaration(string identity) =>
        new(identity, [Stage(identity + ".vsh", ShaderStageKind.Vertex), Stage(identity + ".fsh", ShaderStageKind.Fragment)], 1);
    #endregion
}
