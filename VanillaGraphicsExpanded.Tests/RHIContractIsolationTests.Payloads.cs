using System.Collections.Immutable;
using System.Reflection;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Rendering.RHI;
using VanillaGraphicsExpanded.Tests.Architecture;

namespace VanillaGraphicsExpanded.Tests;

/// <summary>Checks transitive data contracts separately from the lifetime owners that execute them.</summary>
public sealed partial class RHIContractIsolationTests
{
    #region Public API
    /// <summary>Private initialization and nested payload storage stay semantic as well as their immediate signatures.</summary>
    [Fact]
    public void ReachableResourcePayloadsContainNoNativeState()
    {
        var contracts = new[] { typeof(RHICommandContext), typeof(IRHIResourceExecution) };
        var roots = contracts.SelectMany(type => type.GetInterfaces().Append(type))
            .SelectMany(type => type.GetMethods(BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
            .SelectMany(method => method.GetParameters().Select(parameter => parameter.ParameterType).Prepend(method.ReturnType));
        // Initialization/canonical schemas also protect facts handed off through generic/private
        // creation services, even if a particular context method does not name the schema directly.
        var schemas = typeof(GpuResource).Assembly.GetTypes().Where(type =>
            type.Namespace == typeof(GpuResource).Namespace && (type.Name.EndsWith("Initialization", StringComparison.Ordinal)
                || type.Name.StartsWith("Gpu", StringComparison.Ordinal) && type.Name.EndsWith("Metadata", StringComparison.Ordinal)));
        var graph = new ContractPayloadGraph(IsProjectType, IsOpaqueExecutionOwner).Inspect(roots.Concat(schemas));
        foreach (var item in graph)
            Assert.False(IsNativeBoundaryType(item.Key), $"{item.Value} reaches native type {item.Key.FullName}.");
        Assert.Contains(typeof(GpuBufferInitialization), graph.Keys);
        Assert.Contains(typeof(GpuShaderInitialization), graph.Keys);
        Assert.Contains(typeof(GpuProgramResourceMetadata), graph.Keys);
        Assert.Contains(typeof(RHIShaderDescription), graph.Keys);
    }

    /// <summary>The traversal must detect fields, properties, container keys and inherited generic payloads.</summary>
    [Theory]
    [InlineData(typeof(NestedNativePayload))]
    [InlineData(typeof(NativeKeyPayload))]
    [InlineData(typeof(InheritedNativePayload))]
    public void PayloadInspectionDetectsIndirectNativeMembers(Type root)
    {
        var graph = new ContractPayloadGraph(IsProjectType, IsOpaqueExecutionOwner).Inspect([root]);
        Assert.Contains(graph.Keys, IsNativeBoundaryType);
        Assert.Contains(graph.Values, path => path.StartsWith(root.Name + ".", StringComparison.Ordinal)
            || path.StartsWith(root.Name + ":", StringComparison.Ordinal));
    }

    /// <summary>Canonical executable resources carry typed meanings and availability rather than native property bags.</summary>
    [Fact]
    public void ReflectedProgramFactsUseTypedSemanticFields()
    {
        var fields = typeof(GpuProgramResourceMetadata).GetProperties(BindingFlags.Instance | BindingFlags.Public);
        Assert.Equal(typeof(ProgramResourceKind), fields.Single(property => property.Name == nameof(GpuProgramResourceMetadata.Interface)).PropertyType);
        Assert.Equal(typeof(ShaderValueType?), fields.Single(property => property.Name == nameof(GpuProgramResourceMetadata.Type)).PropertyType);
        Assert.Equal(typeof(ShaderStageMask?), fields.Single(property => property.Name == nameof(GpuProgramResourceMetadata.ReferencedStages)).PropertyType);
        var storage = fields.Select(property => property.PropertyType).Concat(typeof(GpuProgramResourceMetadata)
            .GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic).Select(field => field.FieldType));
        Assert.DoesNotContain(storage, type => type == typeof(object)
            || type != typeof(string) && typeof(System.Collections.IEnumerable).IsAssignableFrom(type));
    }

    /// <summary>The restoration token is private implementation state, never a portable boundary declaration.</summary>
    [Fact]
    public void RestorationImplementationRemainsBehindTheContext()
    {
        var native = typeof(RHIBoundaryScope).GetProperty("Native", BindingFlags.Instance | BindingFlags.NonPublic)!;
        Assert.Equal(typeof(EngineBoundaryScope), native.PropertyType);
        Assert.False(native.GetMethod!.IsPublic);
        foreach (var method in typeof(RHICommandContext).GetMethods(BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public))
        {
            Assert.NotEqual(typeof(EngineBoundaryScope), method.ReturnType);
            Assert.DoesNotContain(method.GetParameters(), parameter => parameter.ParameterType == typeof(EngineBoundaryScope));
        }
    }
    #endregion

    #region Private
    /// <summary>Restricts traversal to code-owned storage instead of inspecting third-party runtime internals.</summary>
    private static bool IsProjectType(Type type) => type.Assembly == typeof(GpuResource).Assembly
        || type.Assembly == typeof(RHIContractIsolationTests).Assembly;

    /// <summary>The existing restoration owner holds native state on behalf of execution, not as a resource payload.</summary>
    private static bool IsOpaqueExecutionOwner(Type type) => type == typeof(EngineBoundaryScope);

    /// <summary>Native enums and concrete backend implementations are not semantic payload contracts.</summary>
    private static bool IsNativeBoundaryType(Type type) => type.Namespace?.StartsWith("OpenTK.", StringComparison.Ordinal) == true
        || type.Namespace?.StartsWith("VanillaGraphicsExpanded.Rendering.RHI.Backends.", StringComparison.Ordinal) == true
        || type.Namespace?.StartsWith("VanillaGraphicsExpanded.Rendering.Integration.OpenGL", StringComparison.Ordinal) == true;

    /// <summary>Exercises a native field hidden inside nested immutable collection/property storage.</summary>
    private sealed record NestedNativePayload(ImmutableArray<NativeLeaf[]> Hidden);
    /// <summary>Exercises native selectors used as collection keys rather than direct record fields.</summary>
    private sealed record NativeKeyPayload(IReadOnlyDictionary<OpenTK.Graphics.OpenGL4.TextureTarget, int> Hidden);
    /// <summary>Exercises a native type carried by a generic base rather than a derived declaration.</summary>
    private sealed class InheritedNativePayload : PayloadBase<OpenTK.Graphics.OpenGL4.ShaderType> { }
    /// <summary>Retains one generic test payload in inherited storage.</summary>
    private class PayloadBase<T> { internal T? Value { get; init; } }
    /// <summary>Retains a selector at a second nesting level for negative traversal qualification.</summary>
    private sealed record NativeLeaf(OpenTK.Graphics.OpenGL4.TextureTarget Target);
    #endregion
}
