using System;
using System.Collections.Immutable;
using System.Reflection.Metadata;

namespace OpenGLInterceptionGenerator.Bindings;

/// <summary>Identifies exact managed OpenTK overloads independently of their native dispatch signatures.</summary>
internal sealed class ManagedSignatureProvider : ISignatureTypeProvider<string, object?>
{
    #region Public API
    /// <inheritdoc />
    public string GetPrimitiveType(PrimitiveTypeCode code) => code switch
    {
        PrimitiveTypeCode.Void => "void", PrimitiveTypeCode.String => "string", PrimitiveTypeCode.Object => "object", PrimitiveTypeCode.Char => "char", PrimitiveTypeCode.Boolean => "bool", PrimitiveTypeCode.Byte => "byte",
        PrimitiveTypeCode.SByte => "sbyte", PrimitiveTypeCode.Int16 => "short", PrimitiveTypeCode.UInt16 => "ushort",
        PrimitiveTypeCode.Int32 => "int", PrimitiveTypeCode.UInt32 => "uint",
        PrimitiveTypeCode.Int64 => "long", PrimitiveTypeCode.UInt64 => "ulong",
        PrimitiveTypeCode.IntPtr => "nint", PrimitiveTypeCode.UIntPtr => "nuint",
        PrimitiveTypeCode.Single => "float", PrimitiveTypeCode.Double => "double",
        _ => throw new FormatException("Unsupported native ABI primitive: " + code)
    };
    /// <inheritdoc />
    public string GetPointerType(string elementType) => elementType + "*";
    /// <inheritdoc />
    public string GetByReferenceType(string elementType) => elementType + "&";
    /// <inheritdoc />
    public string GetModifiedType(string modifier, string unmodifiedType, bool isRequired) => unmodifiedType;
    /// <inheritdoc />
    public string GetPinnedType(string elementType) => elementType;
    /// <inheritdoc />
    public string GetTypeFromDefinition(MetadataReader reader, TypeDefinitionHandle handle, byte rawTypeKind)
    {
        var type = reader.GetTypeDefinition(handle);
        return reader.GetString(type.Namespace) + "." + reader.GetString(type.Name);
    }
    /// <inheritdoc />
    public string GetTypeFromReference(MetadataReader reader, TypeReferenceHandle handle, byte rawTypeKind)
    {
        var type = reader.GetTypeReference(handle);
        return reader.GetString(type.Namespace) + "." + reader.GetString(type.Name);
    }
    /// <inheritdoc />
    public string GetTypeFromSpecification(MetadataReader reader, object? context, TypeSpecificationHandle handle, byte rawTypeKind)
        => reader.GetTypeSpecification(handle).DecodeSignature(this, context);
    /// <inheritdoc />
    public string GetGenericMethodParameter(object? context, int index) => throw Unsupported();
    /// <inheritdoc />
    public string GetGenericTypeParameter(object? context, int index) => throw Unsupported();
    /// <inheritdoc />
    public string GetGenericInstantiation(string genericType, ImmutableArray<string> arguments) => throw Unsupported();
    /// <inheritdoc />
    public string GetSZArrayType(string elementType) => elementType + "[]";
    /// <inheritdoc />
    public string GetArrayType(string elementType, ArrayShape shape) => elementType + "[" + new string(',', shape.Rank - 1) + "]";
    /// <inheritdoc />
    public string GetFunctionPointerType(MethodSignature<string> signature) => throw Unsupported();
    #endregion

    #region Private
    /// <summary>Rejects managed or unrecognized values at an unmanaged dispatch boundary.</summary>
    private static FormatException Unsupported() => new("Unsupported managed type in native calli signature.");
    #endregion
}
