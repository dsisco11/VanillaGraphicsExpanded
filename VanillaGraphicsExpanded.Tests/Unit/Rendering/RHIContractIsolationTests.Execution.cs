using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Rendering.ProgramBinaries;
using VanillaGraphicsExpanded.Rendering.Pipeline;
using VanillaGraphicsExpanded.Rendering.Integration;
using VanillaGraphicsExpanded.Rendering.Integration.OpenGL;
using VanillaGraphicsExpanded.Rendering.RHI;

namespace VanillaGraphicsExpanded.Tests.Unit.Rendering;

/// <summary>Checks concrete execution selection and opaque native payloads at their intentional boundaries.</summary>
public sealed partial class RHIContractIsolationTests
{
    private static readonly Lazy<byte[]> runtimePe = new(() => File.ReadAllBytes(typeof(GpuResource).Assembly.Location));
    #region Public API
    /// <summary>Only the context's thin composition members may select concrete backend/cache implementations.</summary>
    [Fact]
    public void SemanticRHISelectsConcreteExecutionOnlyAtComposition()
    {
        // Arrange: only the documented context composition members may select concrete backends.
        var selection = new HashSet<string> { "get_Current", "RequirePreparationAdmission", "get_ResourceOperations" };
        // Act: inspect managed RHI method operands and retain only concrete implementation references.
        foreach (var type in typeof(RHICommandContext).Assembly.GetTypes().Where(type =>
            type.Namespace?.StartsWith("VanillaGraphicsExpanded.Rendering.RHI", StringComparison.Ordinal) == true
            && !IsNativeImplementation(type)))
        foreach (var method in DeclaredBodies(type))
        foreach (var member in ReadReferencedMembers(method).Where(member => IsNativeImplementation(MemberType(member))))
        {
            // Assert: each native selection comes from one of the allowed context members.
            Assert.True(type == typeof(RHICommandContext) && selection.Contains(method.Name),
                $"{type.Name}.{method.Name} selects native implementation {member} outside context composition.");
        }
    }

    /// <summary>Owner and metadata services must use RHI execution rather than hiding a native call behind semantic arguments.</summary>
    [Fact]
    public void ResourceOwnersAndManagerDoNotExecuteNativeImplementations()
    {
        // Arrange: enumerate owners that expose managed resource lifecycle behavior.
        // Act: inspect their declared method operands.
        foreach (var type in typeof(GpuResource).Assembly.GetTypes().Where(IsManagedResourceOwner))
        foreach (var method in DeclaredBodies(type))
        foreach (var member in ReadReferencedMembers(method))
            // Assert: owners use the RHI boundary instead of directly invoking native implementations.
            Assert.False(IsNativeImplementation(MemberType(member)),
                $"{type.Name}.{method.Name} bypasses RHI through {member}.");
    }

    /// <summary>Geometry/reload helpers share context admission rather than choosing cache policy independently.</summary>
    [Theory]
    [InlineData(typeof(ManagedGraphicsGeometry))]
    [InlineData(typeof(EngineFullscreenGeometry))]
    [InlineData(typeof(EngineLiquidPoolGeometry))]
    [InlineData(typeof(EngineGeometryInputs))]
    [InlineData(typeof(ClientRenderingLifetime))]
    public void PreparationHelpersUseTheRHIAdmissionGate(Type owner)
    {
        // Arrange: the theory supplies one geometry or reload helper.
        // Act: enumerate references from its declared executable bodies.
        var members = DeclaredBodies(owner).SelectMany(ReadReferencedMembers).ToArray();
        // Assert: admission is centralized in the semantic context, not a backend-specific gate.
        Assert.Contains(members, member => member.DeclaringType == typeof(RHICommandContext)
            && member.Name == "RequirePreparationAdmission");
        Assert.DoesNotContain(members, member => member.Name == "RequireOutsideEngineBoundary"
            && IsNativeImplementation(MemberType(member)));
    }

    /// <summary>Executable format identifiers stay private opaque cache data, consumed by only native realization and cache persistence.</summary>
    [Fact]
    public void ShaderBinaryFormatRemainsPrivateAndOpaque()
    {
        // Arrange: inspect the executable binary's private format metadata.
        var format = typeof(RHIShaderBinary).GetProperty("NativeFormat", BindingFlags.Instance | BindingFlags.NonPublic)!;
        // Assert: callers cannot construct or read that native payload through a public API.
        Assert.NotNull(format);
        Assert.True(format.GetMethod!.IsAssembly);
        Assert.Null(typeof(RHIShaderBinary).GetProperty("NativeFormat", BindingFlags.Instance | BindingFlags.Public));
        Assert.All(typeof(RHIShaderBinary).GetConstructors(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic),
            constructor => Assert.True(constructor.IsAssembly));

        // Act: find production methods that actually read the private getter.
        var readers = FindGetterReaders(format.GetMethod).ToArray();
        // Assert: only cache persistence or native realization interprets the format.
        Assert.NotEmpty(readers);
        Assert.Contains(readers, type => type == (typeof(DriverProgramCache).Namespace!, nameof(DriverProgramCache)));
        Assert.All(readers, type => Assert.True(type == (typeof(DriverProgramCache).Namespace!, nameof(DriverProgramCache))
            || type.Namespace.StartsWith("VanillaGraphicsExpanded.Rendering.RHI.Backends.", StringComparison.Ordinal),
            $"{type.Namespace}.{type.Name} interprets the private binary-format payload."));
    }

    /// <summary>Platform handles retain typed kind/ownership without exposing native binding selector types.</summary>
    [Fact]
    public void ExternalHandlesCarrySemanticImportIntent()
    {
        // Arrange: inspect the semantic external-handle API and public factories.
        // Act: read declared handle properties, ownership traits, and factory signatures.
        // Assert: import meaning stays typed and native object names remain private to adoption.
        Assert.Equal(typeof(ExternalHandleKind), typeof(RHIExternalHandle).GetProperty(nameof(RHIExternalHandle.Kind))!.PropertyType);
        Assert.Equal(typeof(ExternalHandleOwnership), typeof(RHIExternalHandle).GetProperty(nameof(RHIExternalHandle.Ownership))!.PropertyType);
        Assert.Equal(typeof(nint), typeof(RHIExternalHandle).GetProperty(nameof(RHIExternalHandle.Value))!.PropertyType);
        Assert.False(typeof(IDisposable).IsAssignableFrom(typeof(RHIExternalHandle)));
        // Native object names are accepted by private adoption operations, not portable factories.
        foreach (var method in typeof(RHICommandContext).GetMethods(BindingFlags.Instance | BindingFlags.Public)
            .Where(method => method.Name.StartsWith("Create", StringComparison.Ordinal)))
            Assert.DoesNotContain(method.GetParameters(), parameter => parameter.Name is "id" or "texture" or "program" or "target");
    }
    #endregion

    #region Private
    /// <summary>Finds exact local getter references without asking the runtime to load unrelated engine/UI signatures.</summary>
    private static IEnumerable<(string Namespace, string Name)> FindGetterReaders(MethodInfo getter)
    {
        Assert.Same(typeof(GpuResource).Assembly, getter.Module.Assembly);
        using var stream = new MemoryStream(runtimePe.Value, writable: false);
        using var pe = new PEReader(stream);
        var metadata = pe.GetMetadataReader();
        foreach (var handle in metadata.MethodDefinitions)
        {
            var method = metadata.GetMethodDefinition(handle);
            if (method.RelativeVirtualAddress == 0) continue;
            var bytes = pe.GetMethodBody(method.RelativeVirtualAddress).GetILBytes()!.ToArray();
            if (!ReadMemberTokens(bytes).Contains(getter.MetadataToken)) continue;
            var type = metadata.GetTypeDefinition(method.GetDeclaringType());
            yield return (metadata.GetString(type.Namespace), metadata.GetString(type.Name));
        }
    }

    /// <summary>Reads declared executable methods and constructors without following arbitrary runtime call graphs.</summary>
    private static IEnumerable<MethodBase> DeclaredBodies(Type type) => type
        .GetMethods(BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly)
        .Cast<MethodBase>().Concat(type.GetConstructors(BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic))
        .Where(method => method.GetMethodBody() is not null);

    /// <summary>Includes partial/nested owner machinery while preserving explicit native adapter and backend responsibilities.</summary>
    private static bool IsManagedResourceOwner(Type type)
    {
        if (IsNativeImplementation(type)) return false;
        if (type == typeof(ManagedGraphicsGeometry)) return true;
        for (var current = type; current is not null; current = current.DeclaringType)
            if (typeof(GpuResource).IsAssignableFrom(current) || current == typeof(RHIResourceManager)
                || current == typeof(RHIResourceDisposalQueue)) return true;
        return false;
    }

    /// <summary>Obtains the owner of either a type operand or a referenced member.</summary>
    private static Type MemberType(MemberInfo member) => member is Type type ? type : member.DeclaringType!;

    /// <summary>Recognizes native binding/execution domains independently of concrete helper class names.</summary>
    private static bool IsNativeImplementation(Type type) => type.Namespace?.StartsWith("OpenTK.", StringComparison.Ordinal) == true
        || type.Namespace?.StartsWith("VanillaGraphicsExpanded.Rendering.RHI.Backends.", StringComparison.Ordinal) == true
        || type.Namespace?.StartsWith("VanillaGraphicsExpanded.Rendering.Integration.OpenGL", StringComparison.Ordinal) == true;
    #endregion
}
