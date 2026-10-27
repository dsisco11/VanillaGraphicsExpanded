using System.Reflection;
using System.Reflection.Emit;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Rendering.Contracts;
using VanillaGraphicsExpanded.Rendering.RHI;

namespace VanillaGraphicsExpanded.Tests;

/// <summary>Protects semantic context and private execution signatures from native binding type leakage.</summary>
public sealed class RHIContractIsolationTests
{
    private static readonly IReadOnlyDictionary<ushort, OpCode> OpcodesByValue = typeof(OpCodes)
        .GetFields(BindingFlags.Public | BindingFlags.Static).Where(field => field.FieldType == typeof(OpCode))
        .Select(field => (OpCode)field.GetValue(null)!).ToDictionary(code => unchecked((ushort)code.Value));
    #region Public API
    /// <summary>Neither consumer admission nor private resource execution exposes OpenTK selector types.</summary>
    [Theory]
    [InlineData(typeof(RHICommandContext))]
    [InlineData(typeof(IRHIResourceExecution))]
    public void ResourceBoundarySignaturesContainNoOpenTKTypes(Type contract)
    {
        foreach (var method in contract.GetMethods(BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
        {
            Assert.False(ContainsNativeType(method.ReturnType), $"{contract.Name}.{method.Name} returns a native binding type.");
            foreach (var parameter in method.GetParameters())
                Assert.False(ContainsNativeType(parameter.ParameterType), $"{contract.Name}.{method.Name} accepts a native binding type at {parameter.Name}.");
        }
    }
    /// <summary>Canonical image schemas cannot reintroduce native enum fields behind semantic method signatures.</summary>
    [Theory]
    [InlineData(typeof(GpuTextureMetadata))]
    [InlineData(typeof(GpuTextureLevelMetadata))]
    [InlineData(typeof(GpuTextureBufferMetadata))]
    [InlineData(typeof(GpuRenderbufferMetadata))]
    [InlineData(typeof(GpuImageMetadata))]
    [InlineData(typeof(GpuImageSelection))]
    [InlineData(typeof(GpuFramebufferMetadata))]
    [InlineData(typeof(GpuFramebuffer.SurfaceMetadata))]
    [InlineData(typeof(GpuFramebufferImageMetadata))]
    [InlineData(typeof(GpuTextureInitialization))]
    [InlineData(typeof(GpuTextureViewInitialization))]
    [InlineData(typeof(GpuBufferTextureInitialization))]
    [InlineData(typeof(GpuRenderbufferInitialization))]
    [InlineData(typeof(GpuFramebufferInitialization))]
    public void ImageSchemasContainNoNativeFields(Type schema)
    {
        foreach (var field in schema.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
            Assert.False(ContainsNativeType(field.FieldType), $"{schema.Name}.{field.Name} stores native binding state.");
    }

    /// <summary>Resource and metadata services must not hide native translation calls behind otherwise portable signatures.</summary>
    [Fact]
    public void ManagedResourceServicesDoNotCallNativeCodecs()
    {
        var assembly = typeof(GpuResource).Assembly;
        foreach (var type in assembly.GetTypes())
        {
            string space = type.Namespace ?? "";
            if (!space.StartsWith("VanillaGraphicsExpanded.Rendering", StringComparison.Ordinal)
                || space.Contains(".Backends.", StringComparison.Ordinal)
                || space.Contains(".Integration.", StringComparison.Ordinal)) continue;
            var methods = type.GetMethods(BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly)
                .Cast<MethodBase>().Concat(type.GetConstructors(BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic));
            foreach (var method in methods)
            {
                if (method.GetMethodBody() is null) continue;
                foreach (var member in ReadReferencedMembers(method))
                    Assert.DoesNotContain(member is Type referenced ? referenced.Name : member.DeclaringType?.Name ?? "",
                        new[] { "OpenGLImageValues", "OpenGLResourceValues", "OpenGLResourceSelectors" });
            }
        }
        Assert.Null(assembly.GetType("VanillaGraphicsExpanded.Rendering.Integration.OpenGL.OpenGLResourceSelectors"));
        Assert.Null(assembly.GetType("VanillaGraphicsExpanded.Rendering.Integration.OpenGL.OpenGLImageValues"));
    }

    /// <summary>The build-time shader schema and runtime dimensions share logical values rather than native tokens.</summary>
    [Fact]
    public void ShaderDimensionsMatchRuntimeSchema()
    {
        Assert.Equal((int)TextureDimension.One, (int)ShaderTextureTarget.Texture1D);
        Assert.Equal((int)TextureDimension.Two, (int)ShaderTextureTarget.Texture2D);
        Assert.Equal((int)TextureDimension.Three, (int)ShaderTextureTarget.Texture3D);
        Assert.Equal((int)TextureDimension.Cube, (int)ShaderTextureTarget.TextureCubeMap);
        Assert.Equal((int)TextureDimension.TwoArray, (int)ShaderTextureTarget.Texture2DArray);
        Assert.Equal((int)TextureDimension.Buffer, (int)ShaderTextureTarget.TextureBuffer);
    }
    #endregion
    #region Private
    /// <summary>Reads operand tokens directly so generic definitions need no dynamic-method import or execution.</summary>
    private static IEnumerable<MemberInfo> ReadReferencedMembers(MethodBase method)
    {
        var bytes = method.GetMethodBody()!.GetILAsByteArray()!;
        for (int offset = 0; offset < bytes.Length;)
        {
            ushort code = bytes[offset++];
            if (code == 0xfe) code = (ushort)(0xfe00 | bytes[offset++]);
            var operand = OpcodesByValue[code].OperandType;
            if (operand is OperandType.InlineMethod or OperandType.InlineField or OperandType.InlineTok or OperandType.InlineType)
            {
                // Supply the definition's generic context when resolving constructed member references.
                var member = method.Module.ResolveMember(BitConverter.ToInt32(bytes, offset),
                    method.DeclaringType?.GetGenericArguments(), method.IsGenericMethod ? method.GetGenericArguments() : null);
                if (member is not null) yield return member;
            }
            offset += operand switch
            {
                OperandType.InlineNone => 0,
                OperandType.ShortInlineBrTarget or OperandType.ShortInlineI or OperandType.ShortInlineVar => 1,
                OperandType.InlineVar => 2,
                OperandType.InlineI8 or OperandType.InlineR => 8,
                OperandType.InlineSwitch => checked(4 + 4 * BitConverter.ToInt32(bytes, offset)),
                _ => 4
            };
        }
    }

    /// <summary>Checks arrays, references and generic containers so native types cannot hide inside collection signatures.</summary>
    private static bool ContainsNativeType(Type type)
    {
        if (type.Namespace?.StartsWith("OpenTK.", StringComparison.Ordinal) == true) return true;
        if (type.HasElementType && ContainsNativeType(type.GetElementType()!)) return true;
        return type.IsGenericType && type.GetGenericArguments().Any(ContainsNativeType);
    }
    #endregion
}
